namespace RuinaoSoftwareWpf;

using RuinaoSoftwareWpf.ApplicationContracts;

/// <summary>
/// 保留旧录帧实现，同时向应用层暴露不包含 OpenCV 类型的控制契约。
/// </summary>
public sealed class LegacyCaptureMediaServiceAdapter : ICaptureMediaService
{
    private readonly ICaptureMediaRecorder recorder;
    private readonly TimeProvider timeProvider;
    private CaptureMediaSession? currentSession;

    public LegacyCaptureMediaServiceAdapter(
        ICaptureMediaRecorder recorder,
        TimeProvider timeProvider)
    {
        this.recorder = recorder;
        this.timeProvider = timeProvider;
        recorder.RecordingCompleted += OnRecordingCompleted;
    }

    public event EventHandler<CaptureMediaCompleted>? Completed;

    public bool IsCapturing => recorder.IsRecording;

    public CaptureMediaSession? CurrentSession => currentSession;

    public async Task<CaptureMediaSession> StartAsync(
        CaptureMediaStartRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var legacySession = await recorder.StartAsync(
            new CaptureRecordingRequest(
                CaptureOutputPathProvider.GetOutputRoot(),
                request.SessionKey,
                request.ModuleCode,
                request.ModuleName,
                request.CameraId),
            cancellationToken);

        currentSession = new CaptureMediaSession(
            legacySession.Id,
            legacySession.SessionKey,
            legacySession.ModuleCode,
            legacySession.ModuleName,
            timeProvider.GetUtcNow());
        return currentSession;
    }

    public async Task StopAsync(
        CaptureMediaStopReason reason,
        string? message = null,
        CancellationToken cancellationToken = default)
    {
        recorder.RequestStop(ToLegacyStatus(reason), message ?? string.Empty);
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
            eventArgs.Session.SessionKey,
            eventArgs.Session.ModuleCode,
            eventArgs.Session.ModuleName,
            timeProvider.GetUtcNow());
        currentSession = null;
        Completed?.Invoke(
            this,
            new CaptureMediaCompleted(
                session,
                FromLegacyStatus(eventArgs.Status),
                eventArgs.Message));
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

    private static CaptureMediaStopReason FromLegacyStatus(string status)
    {
        return status switch
        {
            "completed" => CaptureMediaStopReason.Completed,
            "discarded" => CaptureMediaStopReason.Discarded,
            "merge_failed" => CaptureMediaStopReason.Failed,
            _ => CaptureMediaStopReason.Interrupted
        };
    }
}
