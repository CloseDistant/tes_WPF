namespace RuinaoSoftwareWpf;

using OpenCvSharp;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;

/// <summary>
/// 单摄像头后台采集管线。
/// 摄像头阻塞读取和人脸近似检测均不占用 WPF Dispatcher；录像固定按 12.5 FPS 采样，
/// 预览处理只保留最新帧，负载升高时丢弃旧预览而不是累积显示延迟。
/// </summary>
public sealed class OpenCvCameraCaptureService : ICameraCaptureService
{
    private static readonly TimeSpan FrameSampleInterval = TimeSpan.FromMilliseconds(80);
    private static readonly NormalizedCameraRect GuideBounds = new(0.19, 0.06, 0.62, 0.88);

    private readonly ICaptureVideoFrameSink videoFrameSink;
    private readonly ILoggingService logger;
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly SemaphoreSlim previewFrameSignal = new(0, 1);
    private readonly ReplacingDisposableSlot<PendingCameraFrame> pendingPreviewFrames = new();
    private readonly ReplacingDisposableSlot<CameraPreviewSnapshot> completedPreviews = new();

    private CancellationTokenSource? captureCancellation;
    private Task captureTask = Task.CompletedTask;
    private Task previewTask = Task.CompletedTask;
    private int isOpenFlag;
    private int recordingEnabledFlag;
    private int recordedFrameCount;
    private long previewSequence;
    private int disposeState;

    public OpenCvCameraCaptureService(
        ICaptureVideoFrameSink videoFrameSink,
        ILoggingService logger)
    {
        this.videoFrameSink = videoFrameSink;
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

        try
        {
            await Task.WhenAll(captureTask, previewTask);
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
            pendingPreviewFrames.Clear();
            completedPreviews.Clear();
        }
    }

    private void CaptureFrames(VideoCapture capture, CancellationToken cancellationToken)
    {
        var frameSampler = new FixedIntervalFrameSampler(FrameSampleInterval, Stopwatch.Frequency);
        using var frame = new Mat();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!capture.Read(frame) || frame.Empty())
                {
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
                    recordedCount));
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.Error("摄像头后台读取失败。", exception);
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
        var faceRect = DetectFaceLikeRegion(frame);
        var faceState = CameraFaceState.NotDetected;
        NormalizedCameraRect? normalizedFace = null;
        if (faceRect is { } face)
        {
            var faceCenter = new OpenCvSharp.Point(face.X + face.Width / 2, face.Y + face.Height / 2);
            var overlapRatio = CalculateOverlapRatio(face, guideRect);
            faceState = guideRect.Contains(faceCenter) && overlapRatio >= 0.85
                ? CameraFaceState.InsideGuide
                : CameraFaceState.OutsideGuide;
            normalizedFace = Normalize(face, frame.Width, frame.Height);
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
                normalizedFace,
                faceState,
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

    private static NormalizedCameraRect Normalize(OpenCvSharp.Rect rect, int width, int height) => new(
        rect.X / (double)width,
        rect.Y / (double)height,
        rect.Width / (double)width,
        rect.Height / (double)height);

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

    private static OpenCvSharp.Rect? DetectFaceLikeRegion(Mat frame)
    {
        using var ycrcb = new Mat();
        using var mask = new Mat();
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new OpenCvSharp.Size(7, 7));

        Cv2.CvtColor(frame, ycrcb, ColorConversionCodes.BGR2YCrCb);
        Cv2.InRange(ycrcb, new Scalar(0, 133, 77), new Scalar(255, 173, 127), mask);
        Cv2.MorphologyEx(mask, mask, MorphTypes.Open, kernel);
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kernel);
        Cv2.GaussianBlur(mask, mask, new OpenCvSharp.Size(5, 5), 0);
        Cv2.FindContours(
            mask,
            out OpenCvSharp.Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes.ApproxSimple);

        OpenCvSharp.Rect? bestRect = null;
        var bestArea = 0d;
        var minArea = frame.Width * frame.Height * 0.015;
        foreach (var contour in contours)
        {
            var rect = Cv2.BoundingRect(contour);
            var area = rect.Width * rect.Height;
            if (area < minArea)
            {
                continue;
            }

            var ratio = rect.Width / (double)Math.Max(rect.Height, 1);
            if (ratio < 0.45 || ratio > 1.6 || area <= bestArea)
            {
                continue;
            }

            bestArea = area;
            bestRect = rect;
        }

        return bestRect;
    }

    private sealed class PendingCameraFrame(
        Mat frame,
        DateTimeOffset capturedAt,
        int recordedFrameCount) : IDisposable
    {
        public Mat Frame { get; } = frame;

        public DateTimeOffset CapturedAt { get; } = capturedAt;

        public int RecordedFrameCount { get; } = recordedFrameCount;

        public void Dispose() => Frame.Dispose();
    }
}
