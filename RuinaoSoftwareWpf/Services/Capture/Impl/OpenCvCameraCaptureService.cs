namespace RuinaoSoftwareWpf;

using OpenCvSharp;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;

/// <summary>
/// 单摄像头后台采集管线。
/// 摄像头阻塞读取、预览转换和人脸模型推理均不占用 WPF Dispatcher；录像固定按 12.5 FPS 采样。
/// 预览和人脸检测分别只保留最新帧，负载升高时丢弃旧分析帧而不是阻塞录像或累积显示延迟。
/// </summary>
public sealed class OpenCvCameraCaptureService : ICameraCaptureService
{
    private static readonly TimeSpan FrameSampleInterval = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan FaceAnalysisInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan FaceAnalysisStaleAfter = TimeSpan.FromSeconds(1);
    private static readonly NormalizedCameraRect GuideBounds = new(0.19, 0.06, 0.62, 0.88);

    private readonly ICaptureVideoFrameSink videoFrameSink;
    private readonly ICameraFaceAnalyzer faceAnalyzer;
    private readonly ILoggingService logger;
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim previewFrameSignal = new(0, 1);
    private readonly SemaphoreSlim faceFrameSignal = new(0, 1);
    private readonly ReplacingDisposableSlot<PendingCameraFrame> pendingPreviewFrames = new();
    private readonly ReplacingDisposableSlot<PendingCameraFrame> pendingFaceFrames = new();
    private readonly ReplacingDisposableSlot<CameraPreviewSnapshot> completedPreviews = new();

    private CancellationTokenSource? captureCancellation;
    private Task captureTask = Task.CompletedTask;
    private Task previewTask = Task.CompletedTask;
    private Task faceAnalysisTask = Task.CompletedTask;
    private CameraFaceAnalysis? latestFaceAnalysis;
    private int isOpenFlag;
    private int recordingEnabledFlag;
    private int recordedFrameCount;
    private long previewSequence;
    private long faceAnalysisSequence;
    private int disposeState;

    public OpenCvCameraCaptureService(
        ICaptureVideoFrameSink videoFrameSink,
        ICameraFaceAnalyzer faceAnalyzer,
        ILoggingService logger)
    {
        this.videoFrameSink = videoFrameSink;
        this.faceAnalyzer = faceAnalyzer;
        this.logger = logger;
    }

    public bool IsOpen => Volatile.Read(ref isOpenFlag) == 1;

    public int RecordedFrameCount => Volatile.Read(ref recordedFrameCount);

    public async Task<bool> OpenAsync(
        int preferredIndex,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposeState) != 0, this);
        await lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            await CloseCoreAsync();
            var capture = await Task.Run(
                () => TryOpenCapture(preferredIndex, cancellationToken),
                CancellationToken.None);
            if (capture is null)
            {
                return false;
            }

            var cancellation = new CancellationTokenSource();
            captureCancellation = cancellation;
            Volatile.Write(ref recordedFrameCount, 0);
            Volatile.Write(ref isOpenFlag, 1);
            captureTask = Task.Factory.StartNew(
                () => CaptureFrames(capture, cancellation.Token),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            previewTask = Task.Factory.StartNew(
                () => ProcessPreviewFrames(cancellation.Token),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            faceAnalysisTask = Task.Factory.StartNew(
                () => ProcessFaceFrames(cancellation.Token),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            return true;
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public bool TryTakeLatestPreview(out CameraPreviewSnapshot snapshot)
    {
        var current = completedPreviews.Take();
        if (current is null)
        {
            snapshot = null!;
            return false;
        }

        snapshot = current;
        return true;
    }

    public void SetRecordingEnabled(bool enabled)
    {
        if (enabled)
        {
            Volatile.Write(ref recordedFrameCount, 0);
            Volatile.Write(ref recordingEnabledFlag, 1);
            return;
        }

        Volatile.Write(ref recordingEnabledFlag, 0);
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        await lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            await CloseCoreAsync();
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposeState, 1) != 0)
        {
            return;
        }

        await lifecycleGate.WaitAsync();
        try
        {
            await CloseCoreAsync();
        }
        finally
        {
            lifecycleGate.Release();
            lifecycleGate.Dispose();
            previewFrameSignal.Dispose();
            faceFrameSignal.Dispose();
        }
    }

    private async Task CloseCoreAsync()
    {
        Volatile.Write(ref recordingEnabledFlag, 0);
        Volatile.Write(ref isOpenFlag, 0);

        var cancellation = captureCancellation;
        captureCancellation = null;
        cancellation?.Cancel();
        SignalPreviewProcessor();
        SignalFaceProcessor();

        try
        {
            await Task.WhenAll(captureTask, previewTask, faceAnalysisTask);
        }
        catch (OperationCanceledException) when (cancellation?.IsCancellationRequested == true)
        {
        }
        catch (Exception exception)
        {
            logger.Error("摄像头后台管线停止失败。", exception);
        }
        finally
        {
            cancellation?.Dispose();
            captureTask = Task.CompletedTask;
            previewTask = Task.CompletedTask;
            faceAnalysisTask = Task.CompletedTask;
            pendingPreviewFrames.Clear();
            pendingFaceFrames.Clear();
            completedPreviews.Clear();
            Volatile.Write(ref latestFaceAnalysis, null);
        }
    }

    private void CaptureFrames(VideoCapture capture, CancellationToken cancellationToken)
    {
        var frameSampler = new FixedIntervalFrameSampler(FrameSampleInterval, Stopwatch.Frequency);
        var consecutiveFailures = 0;
        using var frame = new Mat();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (!capture.Read(frame) || frame.Empty())
                    {
                        consecutiveFailures++;
                        if (consecutiveFailures == 1 || consecutiveFailures % 50 == 0)
                        {
                            logger.Warning(
                                $"摄像头暂未返回有效帧，将继续尝试：consecutiveFailures={consecutiveFailures}");
                        }

                        cancellationToken.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(10));
                        continue;
                    }

                    var now = Stopwatch.GetTimestamp();
                    if (!frameSampler.ShouldSample(now))
                    {
                        continue;
                    }

                    var recordedCount = RecordedFrameCount;
                    if (Volatile.Read(ref recordingEnabledFlag) == 1)
                    {
                        recordedCount = videoFrameSink.RecordFrame(frame);
                        Volatile.Write(ref recordedFrameCount, recordedCount);
                    }

                    PublishPreviewFrame(new PendingCameraFrame(
                        frame.Clone(),
                        DateTimeOffset.Now,
                        now,
                        recordedCount));
                    consecutiveFailures = 0;
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    consecutiveFailures++;
                    if (consecutiveFailures == 1 || consecutiveFailures % 50 == 0)
                    {
                        logger.Error(
                            $"摄像头后台采集发生瞬时失败，将继续尝试：consecutiveFailures={consecutiveFailures}",
                            exception);
                    }

                    cancellationToken.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(50));
                }
            }
        }
        finally
        {
            capture.Release();
            capture.Dispose();
            Volatile.Write(ref isOpenFlag, 0);
        }
    }

    private void ProcessPreviewFrames(CancellationToken cancellationToken)
    {
        var faceAnalysisSampler = new FixedIntervalFrameSampler(
            FaceAnalysisInterval,
            Stopwatch.Frequency);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                previewFrameSignal.Wait(cancellationToken);
                var pending = TakeLatestPreviewFrame();
                if (pending is null)
                {
                    continue;
                }

                using (pending)
                {
                    try
                    {
                        var snapshot = CreatePreviewSnapshot(pending);
                        completedPreviews.Publish(snapshot);
                        if (faceAnalysisSampler.ShouldSample(pending.MonotonicTimestamp))
                        {
                            PublishFaceFrame(new PendingCameraFrame(
                                pending.Frame.Clone(),
                                pending.CapturedAt,
                                pending.MonotonicTimestamp,
                                pending.RecordedFrameCount));
                        }
                    }
                    catch (Exception exception)
                    {
                        logger.Error("摄像头预览帧处理失败。", exception);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private CameraPreviewSnapshot CreatePreviewSnapshot(PendingCameraFrame pending)
    {
        var frame = pending.Frame;
        var guideRect = GuideRectFor(frame);
        var analysis = Volatile.Read(ref latestFaceAnalysis);
        if (analysis is null
            || Stopwatch.GetElapsedTime(analysis.AnalyzedAtTimestamp, Stopwatch.GetTimestamp()) > FaceAnalysisStaleAfter)
        {
            analysis = CameraFaceAnalysis.Unavailable(
                analysis?.Sequence ?? 0,
                pending.MonotonicTimestamp,
                pending.CapturedAt);
        }

        var isPrimaryFaceInsideGuide = false;
        if (analysis.PrimaryFaceBounds is { } normalizedFace)
        {
            var face = Denormalize(normalizedFace, frame.Width, frame.Height);
            var faceCenter = new OpenCvSharp.Point(face.X + face.Width / 2, face.Y + face.Height / 2);
            var overlapRatio = CalculateOverlapRatio(face, guideRect);
            isPrimaryFaceInsideGuide = guideRect.Contains(faceCenter) && overlapRatio >= 0.85;
        }

        using var bgra = new Mat();
        Cv2.CvtColor(frame, bgra, ColorConversionCodes.BGR2BGRA);
        var stride = checked((int)bgra.Step());
        var pixelLength = checked(stride * bgra.Height);
        var pixels = ArrayPool<byte>.Shared.Rent(pixelLength);
        try
        {
            Marshal.Copy(bgra.Data, pixels, 0, pixelLength);
            return new CameraPreviewSnapshot(
                Interlocked.Increment(ref previewSequence),
                pending.CapturedAt,
                bgra.Width,
                bgra.Height,
                stride,
                pixels,
                pixelLength,
                GuideBounds,
                analysis.PrimaryFaceBounds,
                analysis.State,
                analysis.FaceCount,
                isPrimaryFaceInsideGuide,
                analysis.Sequence,
                pending.RecordedFrameCount);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(pixels);
            throw;
        }
    }

    private void PublishPreviewFrame(PendingCameraFrame frame)
    {
        pendingPreviewFrames.Publish(frame);
        SignalPreviewProcessor();
    }

    private PendingCameraFrame? TakeLatestPreviewFrame() => pendingPreviewFrames.Take();

    private void PublishFaceFrame(PendingCameraFrame frame)
    {
        pendingFaceFrames.Publish(frame);
        SignalFaceProcessor();
    }

    private void ProcessFaceFrames(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                faceFrameSignal.Wait(cancellationToken);
                var pending = pendingFaceFrames.Take();
                if (pending is null)
                {
                    continue;
                }

                using (pending)
                {
                    var analysis = faceAnalyzer.Analyze(
                        pending.Frame,
                        Interlocked.Increment(ref faceAnalysisSequence),
                        pending.MonotonicTimestamp,
                        pending.CapturedAt);
                    Volatile.Write(ref latestFaceAnalysis, analysis);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.Error("人脸检测后台线程异常结束。", exception);
            Volatile.Write(
                ref latestFaceAnalysis,
                CameraFaceAnalysis.Unavailable(
                    Interlocked.Increment(ref faceAnalysisSequence),
                    Stopwatch.GetTimestamp(),
                    DateTimeOffset.Now));
        }
    }

    private void SignalPreviewProcessor()
    {
        if (previewFrameSignal.CurrentCount != 0)
        {
            return;
        }

        try
        {
            previewFrameSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // 并发发布只需要保留一个唤醒信号；最新帧已经在槽位中。
        }
    }

    private void SignalFaceProcessor()
    {
        if (faceFrameSignal.CurrentCount != 0)
        {
            return;
        }

        try
        {
            faceFrameSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // 最新检测帧已经在单槽中，并发发布只需要一个唤醒信号。
        }
    }

    private static VideoCapture? TryOpenCapture(
        int preferredIndex,
        CancellationToken cancellationToken)
    {
        var indices = Enumerable.Range(0, 6)
            .Prepend(preferredIndex)
            .Distinct()
            .Where(index => index >= 0);

        foreach (var index in indices)
        {
            foreach (var api in new[] { VideoCaptureAPIs.DSHOW, VideoCaptureAPIs.MSMF, VideoCaptureAPIs.ANY })
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidate = new VideoCapture(index, api);
                try
                {
                    if (candidate.IsOpened() && HasReadableFrame(candidate, cancellationToken))
                    {
                        return candidate;
                    }
                }
                catch
                {
                    candidate.Release();
                    candidate.Dispose();
                    throw;
                }

                candidate.Release();
                candidate.Dispose();
            }
        }

        return null;
    }

    private static bool HasReadableFrame(
        VideoCapture candidate,
        CancellationToken cancellationToken)
    {
        using var frame = new Mat();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.Read(frame) && !frame.Empty())
            {
                return true;
            }
        }

        return false;
    }

    private static OpenCvSharp.Rect GuideRectFor(Mat frame) => new(
        (int)Math.Round(frame.Width * GuideBounds.X),
        (int)Math.Round(frame.Height * GuideBounds.Y),
        (int)Math.Round(frame.Width * GuideBounds.Width),
        (int)Math.Round(frame.Height * GuideBounds.Height));

    private static OpenCvSharp.Rect Denormalize(NormalizedCameraRect rect, int width, int height) => new(
        (int)Math.Round(rect.X * width),
        (int)Math.Round(rect.Y * height),
        Math.Max(1, (int)Math.Round(rect.Width * width)),
        Math.Max(1, (int)Math.Round(rect.Height * height)));

    private static double CalculateOverlapRatio(OpenCvSharp.Rect face, OpenCvSharp.Rect guide)
    {
        var left = Math.Max(face.Left, guide.Left);
        var top = Math.Max(face.Top, guide.Top);
        var right = Math.Min(face.Right, guide.Right);
        var bottom = Math.Min(face.Bottom, guide.Bottom);

        if (right <= left || bottom <= top)
        {
            return 0;
        }

        var overlapArea = (right - left) * (bottom - top);
        var faceArea = Math.Max(face.Width * face.Height, 1);
        return overlapArea / (double)faceArea;
    }

    private sealed class PendingCameraFrame(
        Mat frame,
        DateTimeOffset capturedAt,
        long monotonicTimestamp,
        int recordedFrameCount) : IDisposable
    {
        public Mat Frame { get; } = frame;

        public DateTimeOffset CapturedAt { get; } = capturedAt;

        public long MonotonicTimestamp { get; } = monotonicTimestamp;

        public int RecordedFrameCount { get; } = recordedFrameCount;

        public void Dispose() => Frame.Dispose();
    }
}
