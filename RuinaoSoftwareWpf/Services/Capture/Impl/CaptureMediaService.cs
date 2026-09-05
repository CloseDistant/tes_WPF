namespace RuinaoSoftwareWpf;

using RuinaoSoftwareWpf.ApplicationContracts;

/// <summary>
/// 向应用层暴露不包含 OpenCV 类型的媒体控制契约。
/// OpenCV 录帧继续由底层 CaptureMediaRecorder 负责。
/// </summary>
internal sealed class CaptureMediaService : ICaptureMediaService
{
    private readonly ICaptureMediaBackend recorder;
    private readonly TimeProvider timeProvider;
    private CaptureMediaSession? currentSession;

    public CaptureMediaService(
        ICaptureMediaBackend recorder,
        TimeProvider timeProvider)
    {
        this.recorder = recorder;
        this.timeProvider = timeProvider;
        recorder.RecordingCompleted += OnRecordingCompleted;
        recorder.AudioLevelAvailable += OnAudioLevelAvailable;
    }

    public event EventHandler<CaptureMediaCompleted>? Completed;

    public event EventHandler<CaptureAudioLevel>? AudioLevelAvailable;

    public bool IsCapturing => recorder.IsRecording;

    public CaptureMediaSession? CurrentSession => currentSession;

    public async Task<CaptureMediaSession> StartAsync(
        CaptureMediaStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var legacySession = await recorder.StartAsync(
            new CaptureRecordingRequest(
                request.AssessmentAttemptId,
                request.SessionKey,
                request.ModuleCode,
                request.ModuleName,
                request.CameraId,
                request.SegmentIndex),
            cancellationToken);

        currentSession = new CaptureMediaSession(
            legacySession.Id,
            legacySession.AssessmentAttemptId,
            legacySession.SessionKey,
            legacySession.ModuleCode,
            legacySession.ModuleName,
            legacySession.OutputDirectory,
            timeProvider.GetUtcNow());
        return currentSession;
    }

    public void RequestStop(
        CaptureMediaStopReason reason,
        string? message = null)
    {
        recorder.RequestStop(ToLegacyStatus(reason), message ?? string.Empty);
    }

    public async Task StopAsync(
        CaptureMediaStopReason reason,
        string? message = null,
        CancellationToken cancellationToken = default)
    {
        RequestStop(reason, message);
        await recorder.WaitForIdleAsync(cancellationToken);
    }

    public Task WaitForIdleAsync(CancellationToken cancellationToken = default)
    {
        return recorder.WaitForIdleAsync(cancellationToken);
    }

    private void OnRecordingCompleted(
        object? sender,
        CaptureRecordingCompletedEventArgs eventArgs)
    {
        var session = currentSession ?? new CaptureMediaSession(
            eventArgs.Session.Id,
            eventArgs.Session.AssessmentAttemptId,
            eventArgs.Session.SessionKey,
            eventArgs.Session.ModuleCode,
            eventArgs.Session.ModuleName,
            eventArgs.Session.OutputDirectory,
            timeProvider.GetUtcNow());
        currentSession = null;
        Completed?.Invoke(
            this,
            new CaptureMediaCompleted(
                session,
                ToCompletionStatus(eventArgs.Status),
                ToErrorCode(eventArgs.Status),
                eventArgs.Message));
    }

    private void OnAudioLevelAvailable(object? sender, CaptureAudioLevel level)
    {
        AudioLevelAvailable?.Invoke(this, level);
    }

    private static string ToLegacyStatus(CaptureMediaStopReason reason)
    {
        return reason switch
        {
            CaptureMediaStopReason.Completed => "completed",
            CaptureMediaStopReason.Discarded => "discarded",
            CaptureMediaStopReason.Failed => "merge_failed",
            _ => "interrupted"
        };
    }

    private static CaptureMediaCompletionStatus ToCompletionStatus(string status)
    {
        return status switch
        {
            "completed" => CaptureMediaCompletionStatus.Completed,
            "completed_with_probe_error" => CaptureMediaCompletionStatus.CompletedWithWarnings,
            "discarded" => CaptureMediaCompletionStatus.Discarded,
            "interrupted" => CaptureMediaCompletionStatus.Interrupted,
            _ => CaptureMediaCompletionStatus.Failed
        };
    }

    private static string? ToErrorCode(string status)
    {
        return status switch
        {
            "completed" or "discarded" or "interrupted" => null,
            "completed_with_probe_error" => "MEDIA_SYNC_PROBE_FAILED",
            "merge_failed" => "MEDIA_MERGE_FAILED",
            "video_write_failed" => "VIDEO_WRITE_FAILED",
            "audio_write_failed" => "AUDIO_WRITE_FAILED",
            "finalize_failed" => "MEDIA_FINALIZE_FAILED",
            _ => "MEDIA_CAPTURE_FAILED"
        };
    }
}
