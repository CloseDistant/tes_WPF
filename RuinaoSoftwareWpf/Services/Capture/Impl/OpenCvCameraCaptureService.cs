namespace RuinaoSoftwareWpf;

using OpenCvSharp;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;

/// <summary>
/// 单摄像头后台采集管线。
/// 摄像头阻塞读取、预览转换和人脸模型推理均不占用 WPF Dispatcher。
/// 预览、录像和人脸分析使用 20/所选录像帧率/5 FPS 三条独立采样链路；预览和分析只保留最新帧，
/// 负载升高时丢弃旧处理帧而不是阻塞录像或累积显示延迟。
/// </summary>
public sealed class OpenCvCameraCaptureService : ICameraCaptureService
{
    private static readonly TimeSpan FaceAnalysisStaleAfter = TimeSpan.FromSeconds(1);
    private static readonly NormalizedCameraRect GuideBounds = new(0.19, 0.06, 0.62, 0.88);
#if DEBUG
    private static readonly bool FaceDiagnosticsEnabled = string.Equals(
        Environment.GetEnvironmentVariable("RUINAO_FACE_DIAGNOSTICS"),
        "1",
        StringComparison.Ordinal);
#endif

    private readonly ICaptureVideoFrameSink videoFrameSink;
    private readonly ICameraFaceAnalyzer faceAnalyzer;
    private readonly ICameraCaptureProfileStore profileStore;
    private readonly ICameraRecordingQualitySettingsService recordingQualitySettings;
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
    private CameraFaceStatusSnapshot? latestFaceStatus;
    private int isOpenFlag;
    private int previewRenderingEnabledFlag = 1;
    private int recordingEnabledFlag;
    private int recordedFrameCount;
    private long previewSequence;
    private long faceAnalysisSequence;
    private CameraCaptureProfileSnapshot? activeProfile;
    private string? lastOpenFailureMessage;
    private CameraCaptureProfile selectedProfile = CameraCaptureProfile.Preferred;
    private int activePreferredIndex = -1;
    private string? activeDeviceKey;
    private double lastPersistedMeasuredSourceFps = double.NaN;
    private int disposeState;

    public OpenCvCameraCaptureService(
        ICaptureVideoFrameSink videoFrameSink,
        ICameraFaceAnalyzer faceAnalyzer,
        ICameraCaptureProfileStore profileStore,
        ICameraRecordingQualitySettingsService recordingQualitySettings,
        ILoggingService logger)
    {
        this.videoFrameSink = videoFrameSink;
        this.faceAnalyzer = faceAnalyzer;
        this.profileStore = profileStore;
        this.recordingQualitySettings = recordingQualitySettings;
        this.logger = logger;
    }

    public bool IsOpen => Volatile.Read(ref isOpenFlag) == 1;

    public int RecordedFrameCount => Volatile.Read(ref recordedFrameCount);

    public CameraCaptureProfileSnapshot? ActiveProfile => Volatile.Read(ref activeProfile);

    public string? LastOpenFailureMessage => Volatile.Read(ref lastOpenFailureMessage);

    public bool IsPreviewRenderingEnabled => Volatile.Read(ref previewRenderingEnabledFlag) == 1;

    public async Task<bool> OpenAsync(
        int preferredIndex,
        string deviceKey,
        bool forceReopen = false,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposeState) != 0, this);
        await recordingQualitySettings.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            Volatile.Write(ref lastOpenFailureMessage, null);
            var requestedProfile = recordingQualitySettings.SelectedProfile;
            if (!forceReopen
                && IsOpen
                && Volatile.Read(ref activePreferredIndex) == preferredIndex
                && string.Equals(activeDeviceKey, deviceKey, StringComparison.OrdinalIgnoreCase)
                && ActiveProfile is { } currentProfile
                && currentProfile.RequestedWidth == requestedProfile.RequestedWidth
                && currentProfile.RequestedHeight == requestedProfile.RequestedHeight
                && Math.Abs(currentProfile.RequestedDeviceFramesPerSecond - requestedProfile.DeviceFramesPerSecond) < 0.1)
            {
                return true;
            }

            var openStartedAt = Stopwatch.GetTimestamp();
            logger.Info(
                $"开始打开摄像头：preferredIndex={preferredIndex}, device={deviceKey}, "
                + $"forceReopen={forceReopen}");
            await CloseCoreAsync();
            selectedProfile = requestedProfile;
            var openedCapture = await Task.Run(
                () => TryOpenCapture(preferredIndex, deviceKey, requestedProfile, cancellationToken),
                CancellationToken.None);
            if (openedCapture is null)
            {
                Volatile.Write(
                    ref lastOpenFailureMessage,
                    $"当前摄像头不支持所选{CameraRecordingQualityCatalog.DisplayName(recordingQualitySettings.SelectedMode)}"
                    + $"（{CameraRecordingQualityCatalog.Specification(recordingQualitySettings.SelectedMode)}），请在高级设置中选择其他档位。");
                logger.Warning(LastOpenFailureMessage!);
                return false;
            }

            var capture = openedCapture.Capture;
            Volatile.Write(ref activePreferredIndex, preferredIndex);
            activeDeviceKey = deviceKey;
            lastPersistedMeasuredSourceFps = double.NaN;
            Volatile.Write(ref activeProfile, openedCapture.Profile);
            videoFrameSink.ConfigureCaptureProfile(openedCapture.Profile);
            faceAnalyzer.Reset();
            logger.Info(
                "摄像头能力档案已应用："
                + $"backend={openedCapture.Profile.CaptureBackend}, "
                + $"requested={(openedCapture.Profile.UsesDriverDefault ? "driver-default" : $"{openedCapture.Profile.RequestedWidth}x{openedCapture.Profile.RequestedHeight}@{openedCapture.Profile.RequestedDeviceFramesPerSecond:0.###}")}, "
                + $"actual={openedCapture.Profile.ActualWidth}x{openedCapture.Profile.ActualHeight}@{openedCapture.Profile.ActualDeviceFramesPerSecond?.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}, "
                + $"codec={openedCapture.Profile.ActualInputCodec ?? "unknown"}, "
                + $"pipelines={openedCapture.Profile.PreviewFramesPerSecond:0.##}/{openedCapture.Profile.RecordingFramesPerSecond:0.##}/{openedCapture.Profile.FaceAnalysisFramesPerSecond:0.##}, "
                + $"firstFrameMs={openedCapture.Profile.OpenToFirstFrameMilliseconds?.ToString("0", System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}, "
                + $"openElapsedMs={Stopwatch.GetElapsedTime(openStartedAt).TotalMilliseconds:0}");
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

    public bool TryTakeLatestFaceStatus(out CameraFaceStatusSnapshot snapshot)
    {
        var current = Interlocked.Exchange(ref latestFaceStatus, null);
        if (current is null)
        {
            snapshot = null!;
            return false;
        }

        snapshot = current;
        return true;
    }

    public void SetPreviewRenderingEnabled(bool enabled)
    {
        Volatile.Write(ref previewRenderingEnabledFlag, enabled ? 1 : 0);
        if (enabled)
        {
            return;
        }

        pendingPreviewFrames.Clear();
        completedPreviews.Clear();
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
            Interlocked.Exchange(ref latestFaceStatus, null);
            Volatile.Write(ref activeProfile, null);
            Volatile.Write(ref activePreferredIndex, -1);
            activeDeviceKey = null;
            lastPersistedMeasuredSourceFps = double.NaN;
        }
    }

    private void CaptureFrames(VideoCapture capture, CancellationToken cancellationToken)
    {
        var previewSampler = new FixedIntervalFrameSampler(
            selectedProfile.PreviewInterval,
            Stopwatch.Frequency,
            earlyToleranceRatio: 0.15);
        var recordingSampler = new FixedIntervalFrameSampler(
            selectedProfile.RecordingInterval,
            Stopwatch.Frequency,
            earlyToleranceRatio: 0.15);
        var faceAnalysisSampler = new FixedIntervalFrameSampler(
            selectedProfile.FaceAnalysisInterval,
            Stopwatch.Frequency,
            earlyToleranceRatio: 0.10);
        var sourceFrameRateTracker = new CameraSourceFrameRateTracker(
            TimeSpan.FromSeconds(5),
            Stopwatch.Frequency);
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
                    if (sourceFrameRateTracker.Observe(now) is { } measurement)
                    {
                        UpdateMeasuredSourceProfile(measurement);
                    }

                    var recordedCount = RecordedFrameCount;
                    if (Volatile.Read(ref recordingEnabledFlag) == 1
                        && recordingSampler.ShouldSample(now))
                    {
                        recordedCount = videoFrameSink.RecordFrame(frame);
                        Volatile.Write(ref recordedFrameCount, recordedCount);
                    }

                    var capturedAt = DateTimeOffset.Now;
                    if (faceAnalysisSampler.ShouldSample(now))
                    {
                        PublishFaceFrame(new PendingCameraFrame(
                            frame.Clone(),
                            capturedAt,
                            now,
                            recordedCount));
                    }

                    if (IsPreviewRenderingEnabled && previewSampler.ShouldSample(now))
                    {
                        PublishPreviewFrame(new PendingCameraFrame(
                            frame.Clone(),
                            capturedAt,
                            now,
                            recordedCount));
                    }

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
                        using var displayFrame = CreateDisplayFrame(pending.Frame);
                        var snapshot = CreatePreviewSnapshot(pending, displayFrame);
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

    private CameraPreviewSnapshot CreatePreviewSnapshot(
        PendingCameraFrame pending,
        Mat displayFrame)
    {
        var frame = displayFrame;
        var analysis = Volatile.Read(ref latestFaceAnalysis);
        if (analysis is null
            || Stopwatch.GetElapsedTime(analysis.AnalyzedAtTimestamp, Stopwatch.GetTimestamp()) > FaceAnalysisStaleAfter)
        {
            analysis = CameraFaceAnalysis.Unavailable(
                analysis?.Sequence ?? 0,
                pending.MonotonicTimestamp,
                pending.CapturedAt);
        }

        var isPrimaryFaceInsideGuide = IsPrimaryFaceInsideGuide(analysis.PrimaryFaceBounds);


#if DEBUG
        if (FaceDiagnosticsEnabled)
        {
            CameraFaceDiagnosticRenderer.Draw(frame, analysis);
        }
#endif

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
                    using var analysisFrame = CreateDisplayFrame(pending.Frame);
                    var analysis = faceAnalyzer.Analyze(
                        analysisFrame,
                        Interlocked.Increment(ref faceAnalysisSequence),
                        pending.MonotonicTimestamp,
                        pending.CapturedAt);
                    Volatile.Write(ref latestFaceAnalysis, analysis);
                    Interlocked.Exchange(
                        ref latestFaceStatus,
                        new CameraFaceStatusSnapshot(
                            analysis.Sequence,
                            pending.CapturedAt,
                            pending.MonotonicTimestamp,
                            analysis.State,
                            IsPrimaryFaceInsideGuide(analysis.PrimaryFaceBounds)));
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
            var unavailable = Volatile.Read(ref latestFaceAnalysis)!;
            Interlocked.Exchange(
                ref latestFaceStatus,
                new CameraFaceStatusSnapshot(
                    unavailable.Sequence,
                    unavailable.CapturedAt,
                    unavailable.AnalyzedAtTimestamp,
                    unavailable.State,
                    false));
        }
    }

    internal static bool IsPrimaryFaceInsideGuide(NormalizedCameraRect? faceBounds)
    {
        if (faceBounds is not { Width: > 0, Height: > 0 } face)
        {
            return false;
        }

        var centerX = face.X + face.Width / 2d;
        var centerY = face.Y + face.Height / 2d;
        var centerInside = centerX >= GuideBounds.X
            && centerX <= GuideBounds.X + GuideBounds.Width
            && centerY >= GuideBounds.Y
            && centerY <= GuideBounds.Y + GuideBounds.Height;
        if (!centerInside)
        {
            return false;
        }

        var overlapLeft = Math.Max(face.X, GuideBounds.X);
        var overlapTop = Math.Max(face.Y, GuideBounds.Y);
        var overlapRight = Math.Min(face.X + face.Width, GuideBounds.X + GuideBounds.Width);
        var overlapBottom = Math.Min(face.Y + face.Height, GuideBounds.Y + GuideBounds.Height);
        var overlapArea = Math.Max(0, overlapRight - overlapLeft) * Math.Max(0, overlapBottom - overlapTop);
        return overlapArea / (face.Width * face.Height) >= 0.85;
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

    private void UpdateMeasuredSourceProfile(CameraSourceFrameRateMeasurement measurement)
    {
        var current = Volatile.Read(ref activeProfile);
        if (current is null)
        {
            return;
        }

        var recordingFramesPerSecond = Math.Clamp(
            measurement.FramesPerSecond,
            1,
            selectedProfile.RecordingFramesPerSecond);
        var measured = current with
        {
            RecordingFramesPerSecond = recordingFramesPerSecond,
            MeasuredSourceFramesPerSecond = measurement.FramesPerSecond,
            MaximumSourceFrameGapMilliseconds = measurement.MaximumFrameGapMilliseconds
        };
        Volatile.Write(ref activeProfile, measured);
        videoFrameSink.ConfigureCaptureProfile(measured);

        logger.Info(
            "摄像头真实到帧率："
            + $"device={activeDeviceKey ?? "unknown"}, "
            + $"sourceFps={measurement.FramesPerSecond:0.00}, "
            + $"frames={measurement.FrameCount}, windowSeconds={measurement.ElapsedSeconds:0.00}, "
            + $"maxFrameGapMs={measurement.MaximumFrameGapMilliseconds:0.0}, "
            + $"recordingTargetFps={recordingFramesPerSecond:0.00}");

        if (measurement.FramesPerSecond < 25)
        {
            logger.Warning(
                "摄像头真实到帧率低于25 FPS："
                + $"device={activeDeviceKey ?? "unknown"}, "
                + $"sourceFps={measurement.FramesPerSecond:0.00}, "
                + "请在开发同步测试中复核画面连续性。");
        }

        if (!string.IsNullOrWhiteSpace(activeDeviceKey)
            && (double.IsNaN(lastPersistedMeasuredSourceFps)
                || Math.Abs(lastPersistedMeasuredSourceFps - measurement.FramesPerSecond) >= 1
                || (lastPersistedMeasuredSourceFps < 25) != (measurement.FramesPerSecond < 25)))
        {
            profileStore.Save(CreateOpeningPreference(activeDeviceKey, measured));
            lastPersistedMeasuredSourceFps = measurement.FramesPerSecond;
        }
    }

    private OpenedCamera? TryOpenCapture(
        int preferredIndex,
        string deviceKey,
        CameraCaptureProfile requestedProfile,
        CancellationToken cancellationToken)
    {
        if (preferredIndex < 0)
        {
            return null;
        }

        // 每个录像档位分别复用已验证后端和性能结果。高分辨率或高帧率档位的
        // 较低实测帧率不能污染均衡档位，否则切回均衡后会错误轮询其他后端。
        var cached = profileStore.Find(deviceKey, requestedProfile.RecordingQualityMode);
        var backendCandidates = new List<VideoCaptureAPIs>();
        VideoCaptureAPIs? lowPerformanceCachedApi = null;
        if (cached is not null
            && Enum.TryParse<VideoCaptureAPIs>(cached.CaptureBackend, ignoreCase: true, out var cachedApi))
        {
            if (cached.MeasuredSourceFramesPerSecond is >= 25)
            {
                backendCandidates.Add(cachedApi);
            }
            else if (cached.MeasuredSourceFramesPerSecond.HasValue)
            {
                lowPerformanceCachedApi = cachedApi;
                logger.Warning(
                    "跳过上次低帧率摄像头后端并尝试统一回退："
                    + $"device={deviceKey}, backend={cachedApi}, "
                    + $"measuredFps={cached.MeasuredSourceFramesPerSecond.Value:0.00}");
            }
            else
            {
                backendCandidates.Add(cachedApi);
            }
        }

        foreach (var api in new[]
                 {
                     VideoCaptureAPIs.DSHOW,
                     VideoCaptureAPIs.MSMF,
                     VideoCaptureAPIs.ANY
                 })
        {
            if (api != lowPerformanceCachedApi
                && !backendCandidates.Contains(api))
            {
                backendCandidates.Add(api);
            }
        }

        if (lowPerformanceCachedApi.HasValue && cached is not null)
        {
            backendCandidates.Add(lowPerformanceCachedApi.Value);
        }

        foreach (var api in backendCandidates)
        {
            // 正式采集只尝试所选设备，并严格请求高级设置中的统一录像档位。
            // 允许切换系统后端，但不允许静默回退到驱动默认分辨率。
            var opened = TryOpenCandidate(
                preferredIndex,
                api,
                requestedProfile,
                cancellationToken);
            if (opened is null)
            {
                continue;
            }

            profileStore.Save(CreateOpeningPreference(deviceKey, opened.Profile));
            return opened;
        }

        return null;
    }

    private static OpenedCamera? TryOpenCandidate(
        int index,
        VideoCaptureAPIs api,
        CameraCaptureProfile requestedProfile,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var firstFrameStartedAt = Stopwatch.GetTimestamp();
        var candidate = new VideoCapture(index, api);
        var accepted = false;
        try
        {
            if (!candidate.IsOpened())
            {
                return null;
            }

            ApplyProfile(candidate, requestedProfile);

            if (!HasReadableFrame(candidate, cancellationToken))
            {
                return null;
            }

            var actualProfile = ReadActualProfile(
                candidate,
                api,
                requestedProfile,
                Stopwatch.GetElapsedTime(firstFrameStartedAt).TotalMilliseconds);
            if (!MatchesRequestedProfile(actualProfile, requestedProfile))
            {
                return null;
            }

            accepted = true;
            return new OpenedCamera(candidate, actualProfile);
        }
        finally
        {
            if (!accepted)
            {
                candidate.Release();
                candidate.Dispose();
            }
        }
    }

    private static void ApplyProfile(
        VideoCapture capture,
        CameraCaptureProfile profile)
    {
        if (TryEncodeFourCc(profile.PreferredInputCodec) is { } fourCc)
        {
            capture.Set(VideoCaptureProperties.FourCC, fourCc);
        }

        capture.Set(VideoCaptureProperties.FrameWidth, profile.RequestedWidth);
        capture.Set(VideoCaptureProperties.FrameHeight, profile.RequestedHeight);
        capture.Set(VideoCaptureProperties.Fps, profile.DeviceFramesPerSecond);

        capture.Set(VideoCaptureProperties.BufferSize, 1);
    }

    private static CameraCaptureProfileSnapshot ReadActualProfile(
        VideoCapture capture,
        VideoCaptureAPIs api,
        CameraCaptureProfile requested,
        double openToFirstFrameMilliseconds)
    {
        var actualFps = capture.Get(VideoCaptureProperties.Fps);
        var actualFourCc = (int)Math.Round(capture.Get(VideoCaptureProperties.FourCC));
        var actualWidth = Math.Max(1, (int)Math.Round(capture.Get(VideoCaptureProperties.FrameWidth)));
        var actualHeight = Math.Max(1, (int)Math.Round(capture.Get(VideoCaptureProperties.FrameHeight)));
        return new CameraCaptureProfileSnapshot(
            requested.RequestedWidth,
            requested.RequestedHeight,
            requested.DeviceFramesPerSecond,
            requested.PreviewFramesPerSecond,
            requested.RecordingFramesPerSecond,
            requested.FaceAnalysisFramesPerSecond,
            requested.PreferredInputCodec,
            actualWidth,
            actualHeight,
            double.IsFinite(actualFps) && actualFps > 0 ? actualFps : null,
            DecodeFourCc(actualFourCc),
            api.ToString(),
            requested.RecordingQualityMode,
            UsesDriverDefault: false,
            openToFirstFrameMilliseconds,
            MeasuredSourceFramesPerSecond: null);
    }

    private static bool MatchesRequestedProfile(
        CameraCaptureProfileSnapshot actual,
        CameraCaptureProfile requested)
    {
        if (actual.ActualWidth != requested.RequestedWidth
            || actual.ActualHeight != requested.RequestedHeight)
        {
            return false;
        }

        return !actual.ActualDeviceFramesPerSecond.HasValue
            || actual.ActualDeviceFramesPerSecond.Value >= requested.DeviceFramesPerSecond * 0.90;
    }

    private static CameraOpeningPreference CreateOpeningPreference(
        string deviceKey,
        CameraCaptureProfileSnapshot profile) => new(
            deviceKey,
            profile.CaptureBackend,
            profile.UsesDriverDefault,
            profile.ActualWidth,
            profile.ActualHeight,
            profile.ActualDeviceFramesPerSecond,
            profile.ActualInputCodec,
            profile.MeasuredSourceFramesPerSecond,
            DateTimeOffset.UtcNow,
            profile.RecordingQualityMode);

    private static int? TryEncodeFourCc(string? codec)
    {
        if (string.IsNullOrWhiteSpace(codec) || codec.Length < 4)
        {
            return null;
        }

        return VideoWriter.FourCC(codec[0], codec[1], codec[2], codec[3]);
    }

    private static string? DecodeFourCc(int fourCc)
    {
        if (fourCc == 0)
        {
            return null;
        }

        var characters = new[]
        {
            (char)(fourCc & 0xff),
            (char)((fourCc >> 8) & 0xff),
            (char)((fourCc >> 16) & 0xff),
            (char)((fourCc >> 24) & 0xff)
        };
        return new string(characters).TrimEnd('\0', ' ');
    }

    private Mat CreateDisplayFrame(Mat source)
        => CreateScaledFrame(source, selectedProfile.PreviewMaximumWidth);

    private static Mat CreateScaledFrame(Mat source, int maximumWidth)
    {
        if (source.Width <= maximumWidth)
        {
            return source.Clone();
        }

        var scale = maximumWidth / (double)source.Width;
        var targetHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
        var result = new Mat();
        Cv2.Resize(
            source,
            result,
            new Size(maximumWidth, targetHeight),
            interpolation: InterpolationFlags.Area);
        return result;
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

    private sealed record OpenedCamera(
        VideoCapture Capture,
        CameraCaptureProfileSnapshot Profile);
}
