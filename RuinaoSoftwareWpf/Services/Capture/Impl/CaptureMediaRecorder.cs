namespace RuinaoSoftwareWpf;

using OpenCvSharp;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text.Json;

/// <summary>
/// 默认音视频录制实现。
/// UI 线程只投递摄像头帧，真正的 VideoWriter 磁盘写入在后台线程完成，避免预览卡顿。
/// </summary>
internal sealed class CaptureMediaRecorder :
    ICaptureMediaBackend,
    ICaptureVideoFrameSink,
    ICaptureFormRecordService
{
    private const int MaximumFrameQueueCapacity = 90;
    private const int MinimumFrameQueueCapacity = 6;
    private const long FrameQueueMemoryBudgetBytes = 384L * 1024 * 1024;

    private readonly ICaptureRecordingRepository repository;
    private readonly ILoggingService logger;
    private readonly IUnifiedSessionService unifiedSessionService;
    private readonly ICaptureVideoFrameWriter videoFrameWriter;
    private readonly ICaptureAudioRecorder audioRecorder;
    private readonly ICaptureMediaEncoder mediaEncoder;
    private readonly ICaptureMediaSyncProbe mediaSyncProbe;
    private readonly object recordingLock = new();

    private BlockingCollection<Mat>? frameQueue;
    private Task<int>? frameWriterTask;
    private CaptureSessionInfo? currentSession;
    private CaptureTimingState? currentTiming;
    private string? videoPath;
    private string? pendingAudioPath;
    private CameraCaptureProfileSnapshot? configuredCaptureProfile;
    private int queuedFrameCount;
    private int isRecordingFlag;
    private Task finalizationTask = Task.CompletedTask;

    public CaptureMediaRecorder(
        ICaptureRecordingRepository repository,
        ILoggingService logger,
        IUnifiedSessionService unifiedSessionService,
        ICaptureVideoFrameWriter videoFrameWriter,
        ICaptureAudioRecorder audioRecorder,
        ICaptureMediaEncoder mediaEncoder,
        ICaptureMediaSyncProbe mediaSyncProbe)
    {
        this.repository = repository;
        this.logger = logger;
        this.unifiedSessionService = unifiedSessionService;
        this.videoFrameWriter = videoFrameWriter;
        this.audioRecorder = audioRecorder;
        this.mediaEncoder = mediaEncoder;
        this.mediaSyncProbe = mediaSyncProbe;
    }

    public event EventHandler<CaptureRecordingCompletedEventArgs>? RecordingCompleted;

    public bool IsRecording => Volatile.Read(ref isRecordingFlag) == 1;

    public string? CurrentModuleName => currentSession?.ModuleName;

    public CaptureSessionInfo? CurrentSession => currentSession;

    public void ConfigureCaptureProfile(CameraCaptureProfileSnapshot profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        lock (recordingLock)
        {
            configuredCaptureProfile = profile;
            // 摄像头打开后约5秒才能得到真实源帧率；若模块已经开始录制，
            // 仍需把实测结果补入本次录制快照和最终事件，而不是只留驱动属性值。
            currentTiming?.RecordCameraProfile(profile);
        }
    }

    public async Task<CaptureSessionInfo> StartAsync(CaptureRecordingRequest request, CancellationToken cancellationToken = default)
    {
        var unifiedSession = await unifiedSessionService.GetOrStartAsync(cancellationToken);
        if (!string.Equals(request.SessionKey, unifiedSession.SessionKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("数字表型录制必须使用当前统一 SessionKey。");
        }

        lock (recordingLock)
        {
            if (IsRecording)
            {
                throw new InvalidOperationException("已有采集录制正在进行，不能重复启动。");
            }

            if (!finalizationTask.IsCompleted)
            {
                throw new InvalidOperationException("上一段采集仍在收尾，请稍后再开始。");
            }
        }

        var outputRoot = CaptureOutputPathProvider.GetOutputRoot();
        var sessionDirectory = Path.Combine(
            outputRoot,
            request.SessionKey,
            request.ModuleCode,
            request.AssessmentAttemptId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "development");
        Directory.CreateDirectory(sessionDirectory);

        var rawVideoPath = Path.Combine(sessionDirectory, $"{request.ModuleCode}_raw.avi");
        var normalizedVideoPath = Path.Combine(sessionDirectory, $"{request.ModuleCode}_normalized.avi");
        var audioPath = Path.Combine(sessionDirectory, $"{request.ModuleCode}.wav");
        var mergedVideoPath = Path.Combine(sessionDirectory, $"{request.ModuleCode}.mp4");
        var recordStartedAt = DateTimeOffset.Now;

        var frameQueueCapacity = CalculateFrameQueueCapacity(configuredCaptureProfile);
        logger.Info(
            $"开始采集录制：module={request.ModuleCode}, session={request.SessionKey}, "
            + $"output={sessionDirectory}, frameQueueCapacity={frameQueueCapacity}");

        CaptureSessionInfo session;
        session = await repository.CreateModuleSessionAsync(
            outputRoot,
            request.AssessmentAttemptId,
            request.SessionKey,
            request.ModuleCode,
            request.ModuleName,
            request.CameraName,
            rawVideoPath,
            normalizedVideoPath,
            audioPath,
            mergedVideoPath,
            cancellationToken);

        var newQueue = new BlockingCollection<Mat>(frameQueueCapacity);
        var timing = new CaptureTimingState(recordStartedAt);
        timing.RecordRawVideoPath(rawVideoPath);
        timing.RecordCameraProfile(configuredCaptureProfile);
        var newWriterTask = Task.Run(() => videoFrameWriter.WriteAsync(
            rawVideoPath,
            newQueue,
            timing));

        lock (recordingLock)
        {
            videoPath = normalizedVideoPath;
            pendingAudioPath = audioPath;
            frameQueue = newQueue;
            frameWriterTask = newWriterTask;
            currentSession = session;
            currentTiming = timing;
            queuedFrameCount = 0;
            Volatile.Write(ref isRecordingFlag, 1);
        }

        await TryRecordTimelineEventAsync(
            "module_recording_started",
            request.ModuleName,
            JsonSerializer.Serialize(new
            {
                request.AssessmentAttemptId,
                request.ModuleCode,
                request.ModuleName,
                request.CameraName,
                cameraProfile = timing.CameraProfile,
                videoEncoding = new
                {
                    rawCodec = "MJPG",
                    finalCodec = "H.264",
                    finalCrf = 20
                }
            }));
        return session;
    }

    public int RecordFrame(Mat frame)
    {
        var frameAt = DateTimeOffset.Now;
        var clonedFrame = frame.Clone();
        CaptureTimingState? timing;
        int count;
        lock (recordingLock)
        {
            if (!IsRecording || frameQueue is null)
            {
                clonedFrame.Dispose();
                return Volatile.Read(ref queuedFrameCount);
            }

            timing = currentTiming;
            timing?.RecordFrameAttempt(frameQueue.Count);
            if (!frameQueue.TryAdd(clonedFrame))
            {
                timing?.RecordFrameDropped(frameQueue.Count);
                clonedFrame.Dispose();
                return Volatile.Read(ref queuedFrameCount);
            }

            count = Interlocked.Increment(ref queuedFrameCount);
            timing?.RecordFrame(frameAt, count);
            if (timing is not null
                && !audioRecorder.IsActive
                && !string.IsNullOrWhiteSpace(pendingAudioPath))
            {
                StartAudioRecordingAfterFirstVideoFrameLocked(timing, frameAt);
            }
        }

        return count;
    }

    public async Task RecordModuleEventAsync(
        string eventType,
        string? message = null,
        string? payloadJson = null,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? endedAt = null,
        CancellationToken cancellationToken = default)
    {
        var session = CurrentSession;
        if (session is null)
        {
            return;
        }

        await repository.RecordModuleEventAsync(session, eventType, message, payloadJson, startedAt, endedAt, cancellationToken);
        await unifiedSessionService.RecordEventAsync(
            SessionModuleCodes.DigitalPhenotype,
            eventType,
            message,
            payloadJson,
            startedAt,
            cancellationToken);
    }

    public async Task RecordModuleEventAsync(
        CaptureSessionInfo session,
        string eventType,
        string? message = null,
        string? payloadJson = null,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? endedAt = null,
        CancellationToken cancellationToken = default)
    {
        await repository.RecordModuleEventAsync(session, eventType, message, payloadJson, startedAt, endedAt, cancellationToken);
        await unifiedSessionService.RecordEventAsync(
            SessionModuleCodes.DigitalPhenotype,
            eventType,
            message,
            payloadJson,
            startedAt,
            cancellationToken);
    }

    public async Task<CaptureFormRecordInfo> SaveFormModuleRecordAsync(
        long assessmentAttemptId,
        string sessionKey,
        string moduleCode,
        string moduleName,
        string formPayloadJson,
        string status = "completed",
        CancellationToken cancellationToken = default)
    {
        var unifiedSession = await unifiedSessionService.GetOrStartAsync(cancellationToken);
        if (!string.Equals(sessionKey, unifiedSession.SessionKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("数字表型表单必须使用当前统一 SessionKey。");
        }

        return await repository.SaveFormModuleRecordAsync(
            CaptureOutputPathProvider.GetOutputRoot(),
            assessmentAttemptId,
            sessionKey,
            moduleCode,
            moduleName,
            formPayloadJson,
            status,
            cancellationToken);
    }

    public void RequestStop(string status, string message)
    {
        CaptureSessionInfo? session;
        Task<int>? writerTask;
        Task audioStopTask;
        BlockingCollection<Mat>? queue;
        CaptureTimingState? timing;

        lock (recordingLock)
        {
            if (!IsRecording)
            {
                return;
            }

            Volatile.Write(ref isRecordingFlag, 0);
            queue = frameQueue;
            writerTask = frameWriterTask;
            session = currentSession;
            timing = currentTiming;
            timing?.Complete(DateTimeOffset.Now, Volatile.Read(ref queuedFrameCount));

            frameQueue = null;
            frameWriterTask = null;
            videoPath = null;
            pendingAudioPath = null;
            currentSession = null;
            currentTiming = null;
        }

        // 在模块结束边界立即向麦克风发出停止请求，但不在 UI 线程等待驱动回调。
        // 视频写入线程随后只负责清空此前已入队的帧，因此不会引入额外音频尾巴。
        audioStopTask = audioRecorder.StopAsync(timing);
        queue?.CompleteAdding();
        logger.Info(
            $"停止采集录制：session={session?.SessionKey}, status={status}, "
            + $"attemptedFrames={timing?.AttemptedFrameCount ?? 0}, "
            + $"queuedFrames={timing?.QueuedFrameCount ?? Volatile.Read(ref queuedFrameCount)}, "
            + $"droppedFrames={timing?.DroppedFrameCount ?? 0}, "
            + $"dropRate={timing?.DroppedFrameRate ?? 0:P2}, "
            + $"maxQueueDepth={timing?.MaximumQueueDepth ?? 0}");
        if (timing is { DroppedFrameRate: > 0.02 })
        {
            logger.Warning(
                $"视频录制丢帧率超过 2%：session={session?.SessionKey}, "
                + $"dropRate={timing.DroppedFrameRate:P2}");
        }

        if (session is not null)
        {
            var finalTiming = timing ?? new CaptureTimingState(DateTimeOffset.Now);
            // 文件就绪检查、驱动停止等待、FFmpeg/ffprobe 与数据库收尾都不得从
            // Dispatcher 回调内联执行，否则倒计时和摄像头预览会一起假死。
            var task = Task.Run(() => CompleteRecordingSafelyAsync(
                session,
                writerTask,
                audioStopTask,
                finalTiming,
                status,
                message));
            lock (recordingLock)
            {
                finalizationTask = task;
            }
        }
    }

    public async Task WaitForIdleAsync(CancellationToken cancellationToken = default)
    {
        Task pending;
        lock (recordingLock)
        {
            pending = finalizationTask;
        }

        await pending.WaitAsync(cancellationToken);
    }

    private async Task CompleteRecordingSafelyAsync(
        CaptureSessionInfo session,
        Task<int>? writerTask,
        Task audioStopTask,
        CaptureTimingState timing,
        string status,
        string message)
    {
        try
        {
            await CompleteRecordingAsync(session, writerTask, audioStopTask, timing, status, message)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.Error($"采集录制收尾发生未处理错误：session={session.SessionKey}", exception);
            try
            {
                await repository.CompleteSessionAsync(session, "finalize_failed", exception.Message)
                    .ConfigureAwait(false);
            }
            catch (Exception repositoryException)
            {
                logger.Error($"采集失败状态写入数据库失败：session={session.SessionKey}", repositoryException);
            }

            await TryRecordTimelineEventAsync(
                "module_recording_finalize_failed",
                session.ModuleName,
                JsonSerializer.Serialize(new { session.ModuleCode, error = exception.Message }))
                .ConfigureAwait(false);
            RecordingCompleted?.Invoke(
                this,
                new CaptureRecordingCompletedEventArgs(
                    session,
                    "finalize_failed",
                    exception.Message));
        }
    }

    private void StartAudioRecordingAfterFirstVideoFrameLocked(
        CaptureTimingState timing,
        DateTimeOffset firstFrameAt)
    {
        if (!IsRecording || string.IsNullOrWhiteSpace(pendingAudioPath) || audioRecorder.IsActive)
        {
            return;
        }

        var audioPath = pendingAudioPath;
        // 第一帧成功进入录像队列即启动音频，不等待磁盘写入，避免队列负载造成开头偏移。
        audioRecorder.Start(audioPath);
        pendingAudioPath = null;
        timing.RecordAudioStarted(DateTimeOffset.Now, firstFrameAt, "after_first_video_frame_queued");
        logger.Info($"音频录制已启动：audioPath={audioPath}");
    }

    private async Task CompleteRecordingAsync(
        CaptureSessionInfo session,
        Task<int>? writerTask,
        Task audioStopTask,
        CaptureTimingState timing,
        string status,
        string message)
    {
        var finalStatus = status;
        var finalMessage = message;
        var writtenFrameCount = 0;

        try
        {
            await audioStopTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            finalStatus = "audio_write_failed";
            finalMessage = $"音频录制停止失败：{exception.Message}";
            logger.Error($"音频录制停止失败：session={session.SessionKey}", exception);
        }

        try
        {
            if (writerTask is not null)
            {
                writtenFrameCount = await writerTask.ConfigureAwait(false);
                logger.Info($"视频帧写入完成：session={session.SessionKey}, writtenFrames={writtenFrameCount}");
            }
        }
        catch (Exception exception)
        {
            finalStatus = "video_write_failed";
            finalMessage = $"视频帧写入失败：{exception.Message}";
            logger.Error($"视频帧写入失败：session={session.SessionKey}", exception);
        }

        if (finalStatus == "completed")
        {
            try
            {
                Exception? lastSaveException = null;
                for (var saveAttempt = 1; saveAttempt <= 2; saveAttempt++)
                {
                    try
                    {
                        var rawVideoPath = timing.RawVideoPath ?? session.RawVideoPath;
                        mediaEncoder.WaitForFileReady(rawVideoPath);
                        mediaEncoder.WaitForFileReady(session.AudioPath);
                        var adjustedFrameRate = await mediaEncoder.CalculateAdjustedFrameRateAsync(session.AudioPath, writtenFrameCount)
                            .ConfigureAwait(false);
                        timing.RecordAdjustedFrameRate(adjustedFrameRate);
                        logger.Info($"开始校正 OpenCV 视频时长：session={session.SessionKey}, adjustedFps={adjustedFrameRate?.ToString(CultureInfo.InvariantCulture) ?? "null"}, saveAttempt={saveAttempt}");
                        await mediaEncoder.NormalizeVideoDurationAsync(rawVideoPath, session.NormalizedVideoPath, adjustedFrameRate)
                            .ConfigureAwait(false);
                        logger.Info($"开始合成音视频：session={session.SessionKey}, saveAttempt={saveAttempt}");
                        await mediaEncoder.MergeAsync(session.NormalizedVideoPath, session.AudioPath, session.MergedVideoPath)
                            .ConfigureAwait(false);
                        logger.Info($"音视频合成完成：session={session.SessionKey}, output={session.MergedVideoPath}, saveAttempt={saveAttempt}");
                        lastSaveException = null;
                        break;
                    }
                    catch (Exception exception) when (saveAttempt == 1)
                    {
                        lastSaveException = exception;
                        logger.Error($"音视频首次保存失败，保留原始文件并自动重试：session={session.SessionKey}", exception);
                    }
                    catch (Exception exception)
                    {
                        lastSaveException = exception;
                    }
                }

                if (lastSaveException is not null)
                {
                    throw new InvalidOperationException("音视频自动重新保存仍失败。", lastSaveException);
                }
            }
            catch (Exception exception)
            {
                finalStatus = "merge_failed";
                finalMessage = $"音视频合成失败：{exception.Message}";
                logger.Error($"音视频合成失败：session={session.SessionKey}", exception);
            }

            if (finalStatus == "completed")
            {
                try
                {
                    var syncProbe = await mediaSyncProbe.ProbeAsync(session, timing, writtenFrameCount)
                        .ConfigureAwait(false);
                    await RecordMediaSyncProbeAsync(session, syncProbe).ConfigureAwait(false);
                    finalMessage = syncProbe.SyncStatus == "warning"
                        ? $"音视频合成完成，同步偏差 {syncProbe.SyncOffsetMs} ms，请复核"
                        : $"音视频合成完成，同步偏差 {syncProbe.SyncOffsetMs} ms";
                    logger.Info($"同步探测完成：session={session.SessionKey}, status={syncProbe.SyncStatus}, offsetMs={syncProbe.SyncOffsetMs}");
                }
                catch (Exception exception)
                {
                    finalStatus = "completed_with_probe_error";
                    finalMessage = $"音视频合成完成，同步校验失败：{exception.Message}";
                    logger.Error($"同步探测失败：session={session.SessionKey}", exception);
                }
            }
        }
        else if (status == "discarded")
        {
            finalStatus = "discarded";
            finalMessage = message;
            mediaEncoder.DeleteDiscardedRecording(session);
        }

        await repository.CompleteSessionAsync(session, finalStatus, finalMessage).ConfigureAwait(false);
        await TryRecordTimelineEventAsync(
            "module_recording_stopped",
            session.ModuleName,
            JsonSerializer.Serialize(new
            {
                session.AssessmentAttemptId,
                session.ModuleCode,
                status = finalStatus,
                message = finalMessage,
                writtenFrameCount,
                timing.AttemptedFrameCount,
                timing.QueuedFrameCount,
                timing.DroppedFrameCount,
                timing.DroppedFrameRate,
                timing.MaximumQueueDepth,
                timing.CameraProfile
            })).ConfigureAwait(false);
        logger.Info($"采集录制收尾完成：session={session.SessionKey}, status={finalStatus}, message={finalMessage}");
        RecordingCompleted?.Invoke(this, new CaptureRecordingCompletedEventArgs(session, finalStatus, finalMessage));
    }

    internal static int CalculateFrameQueueCapacity(CameraCaptureProfileSnapshot? profile)
    {
        if (profile is null)
        {
            return MaximumFrameQueueCapacity;
        }

        var bytesPerFrame = Math.Max(
            1L,
            (long)profile.ActualWidth * profile.ActualHeight * 3);
        return Math.Clamp(
            (int)(FrameQueueMemoryBudgetBytes / bytesPerFrame),
            MinimumFrameQueueCapacity,
            MaximumFrameQueueCapacity);
    }

    private async Task TryRecordTimelineEventAsync(string eventType, string message, string payloadJson)
    {
        try
        {
            if (unifiedSessionService.CurrentSession is not null)
            {
                await unifiedSessionService.RecordEventAsync(
                    SessionModuleCodes.DigitalPhenotype,
                    eventType,
                    message,
                    payloadJson,
                    cancellationToken: CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            logger.Error($"数字表型统一时间轴事件写入失败：eventType={eventType}", exception);
        }
    }

    private async Task RecordMediaSyncProbeAsync(CaptureSessionInfo session, MediaSyncProbeResult probe)
    {
        var payloadJson = JsonSerializer.Serialize(probe);
        await repository.RecordModuleEventAsync(
            session,
            "media_sync_probe",
            probe.SyncStatus == "warning" ? "音视频同步偏差超过阈值" : "音视频同步校验通过",
            payloadJson,
            probe.RecordStartedAtUnixMs.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(probe.RecordStartedAtUnixMs.Value) : null,
            probe.RecordEndedAtUnixMs.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(probe.RecordEndedAtUnixMs.Value) : null);
    }

}
