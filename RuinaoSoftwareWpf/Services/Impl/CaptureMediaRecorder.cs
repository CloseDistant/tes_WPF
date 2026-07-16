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
internal sealed class CaptureMediaRecorder : ICaptureMediaRecorder
{
    private const int FrameQueueCapacity = 90;

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

        var sessionDirectory = Path.Combine(request.OutputRoot, request.SessionKey, request.ModuleCode);
        Directory.CreateDirectory(sessionDirectory);

        var rawVideoPath = Path.Combine(sessionDirectory, $"{request.ModuleCode}_raw.avi");
        var normalizedVideoPath = Path.Combine(sessionDirectory, $"{request.ModuleCode}_normalized.avi");
        var audioPath = Path.Combine(sessionDirectory, $"{request.ModuleCode}.wav");
        var mergedVideoPath = Path.Combine(sessionDirectory, $"{request.ModuleCode}.mp4");
        var recordStartedAt = DateTimeOffset.Now;

        logger.Info($"开始采集录制：module={request.ModuleCode}, session={request.SessionKey}, output={sessionDirectory}");

        CaptureSessionInfo session;
        session = await repository.CreateModuleSessionAsync(
            request.OutputRoot,
            request.SessionKey,
            request.ModuleCode,
            request.ModuleName,
            request.CameraName,
            rawVideoPath,
            normalizedVideoPath,
            audioPath,
            mergedVideoPath,
            cancellationToken);

        var newQueue = new BlockingCollection<Mat>(FrameQueueCapacity);
        var timing = new CaptureTimingState(recordStartedAt);
        timing.RecordRawVideoPath(rawVideoPath);
        var newWriterTask = Task.Run(() => videoFrameWriter.WriteAsync(
            rawVideoPath,
            newQueue,
            timing,
            writtenAt => StartAudioRecordingAfterFirstVideoFrame(timing, writtenAt)));

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
            JsonSerializer.Serialize(new { request.ModuleCode, request.ModuleName, request.CameraName }));
        return session;
    }

    public int RecordFrame(Mat frame)
    {
        BlockingCollection<Mat>? queue;
        lock (recordingLock)
        {
            if (!IsRecording || frameQueue is null)
            {
                return Volatile.Read(ref queuedFrameCount);
            }

            queue = frameQueue;
        }

        var frameAt = DateTimeOffset.Now;
        var clonedFrame = frame.Clone();
        if (queue.TryAdd(clonedFrame))
        {
            var count = Interlocked.Increment(ref queuedFrameCount);
            lock (recordingLock)
            {
                currentTiming?.RecordFrame(frameAt, count);
            }

            return count;
        }

        // 队列满时丢弃当前帧，优先保证 UI 不被磁盘写入拖慢。
        clonedFrame.Dispose();
        return Volatile.Read(ref queuedFrameCount);
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
        string outputRoot,
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

        return await repository.SaveFormModuleRecordAsync(outputRoot, sessionKey, moduleCode, moduleName, formPayloadJson, status, cancellationToken);
    }

    public void RequestStop(string status, string message)
    {
        CaptureSessionInfo? session;
        Task<int>? writerTask;
        BlockingCollection<Mat>? queue;
        CaptureTimingState? timing;
        var stopAudioAfterVideoWriter = status == "completed";

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

        queue?.CompleteAdding();
        logger.Info($"停止采集录制：session={session?.SessionKey}, status={status}, queuedFrames={Volatile.Read(ref queuedFrameCount)}");

        if (!stopAudioAfterVideoWriter)
        {
            audioRecorder.Stop(timing);
        }

        if (session is not null)
        {
            var task = CompleteRecordingSafelyAsync(
                session,
                writerTask,
                timing ?? new CaptureTimingState(DateTimeOffset.Now),
                status,
                message,
                stopAudioAfterVideoWriter);
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
        CaptureTimingState timing,
        string status,
        string message,
        bool stopAudioAfterVideoWriter)
    {
        try
        {
            await CompleteRecordingAsync(session, writerTask, timing, status, message, stopAudioAfterVideoWriter);
        }
        catch (Exception exception)
        {
            logger.Error($"采集录制收尾发生未处理错误：session={session.SessionKey}", exception);
            try
            {
                await repository.CompleteSessionAsync(session, "finalize_failed", exception.Message);
            }
            catch (Exception repositoryException)
            {
                logger.Error($"采集失败状态写入数据库失败：session={session.SessionKey}", repositoryException);
            }

            await TryRecordTimelineEventAsync(
                "module_recording_finalize_failed",
                session.ModuleName,
                JsonSerializer.Serialize(new { session.ModuleCode, error = exception.Message }));
        }
    }

    private void StartAudioRecordingAfterFirstVideoFrame(CaptureTimingState timing, DateTimeOffset firstFrameWrittenAt)
    {
        string? audioPath;
        lock (recordingLock)
        {
            if (!IsRecording || string.IsNullOrWhiteSpace(pendingAudioPath) || audioRecorder.IsActive)
            {
                return;
            }

            audioPath = pendingAudioPath;
            pendingAudioPath = null;
        }

        // 音频从视频第一帧落盘后启动，减少“音频先开始、视频后开始”造成的整体偏移。
        audioRecorder.Start(audioPath);
        timing.RecordAudioStarted(DateTimeOffset.Now, firstFrameWrittenAt, "after_first_video_frame_written");
        logger.Info($"音频录制已启动：audioPath={audioPath}");
    }

    private async Task CompleteRecordingAsync(
        CaptureSessionInfo session,
        Task<int>? writerTask,
        CaptureTimingState timing,
        string status,
        string message,
        bool stopAudioAfterVideoWriter)
    {
        var finalStatus = status;
        var finalMessage = message;
        var writtenFrameCount = 0;

        try
        {
            if (writerTask is not null)
            {
                writtenFrameCount = await writerTask;
                logger.Info($"视频帧写入完成：session={session.SessionKey}, writtenFrames={writtenFrameCount}");
            }
        }
        catch (Exception exception)
        {
            finalStatus = "video_write_failed";
            finalMessage = $"视频帧写入失败：{exception.Message}";
            logger.Error($"视频帧写入失败：session={session.SessionKey}", exception);
        }

        if (stopAudioAfterVideoWriter)
        {
            audioRecorder.Stop(timing);
        }

        if (finalStatus == "completed")
        {
            try
            {
                var rawVideoPath = timing.RawVideoPath ?? session.RawVideoPath;
                mediaEncoder.WaitForFileReady(rawVideoPath);
                mediaEncoder.WaitForFileReady(session.AudioPath);
                var adjustedFrameRate = await mediaEncoder.CalculateAdjustedFrameRateAsync(session.AudioPath, writtenFrameCount);
                timing.RecordAdjustedFrameRate(adjustedFrameRate);
                logger.Info($"开始校正 OpenCV 视频时长：session={session.SessionKey}, adjustedFps={adjustedFrameRate?.ToString(CultureInfo.InvariantCulture) ?? "null"}");
                await mediaEncoder.NormalizeVideoDurationAsync(rawVideoPath, session.NormalizedVideoPath, adjustedFrameRate);
                logger.Info($"开始合成音视频：session={session.SessionKey}");
                await mediaEncoder.MergeAsync(session.NormalizedVideoPath, session.AudioPath, session.MergedVideoPath);
                logger.Info($"音视频合成完成：session={session.SessionKey}, output={session.MergedVideoPath}");
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
                    var syncProbe = await mediaSyncProbe.ProbeAsync(session, timing, writtenFrameCount);
                    await RecordMediaSyncProbeAsync(session, syncProbe);
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

        await repository.CompleteSessionAsync(session, finalStatus, finalMessage);
        await TryRecordTimelineEventAsync(
            "module_recording_stopped",
            session.ModuleName,
            JsonSerializer.Serialize(new
            {
                session.ModuleCode,
                status = finalStatus,
                message = finalMessage,
                writtenFrameCount
            }));
        logger.Info($"采集录制收尾完成：session={session.SessionKey}, status={finalStatus}, message={finalMessage}");
        RecordingCompleted?.Invoke(this, new CaptureRecordingCompletedEventArgs(session, finalStatus, finalMessage));
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
