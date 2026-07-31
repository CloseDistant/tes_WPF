namespace RuinaoSoftwareWpf.ApplicationContracts;

public enum CaptureMediaStopReason
{
    Completed,
    Interrupted,
    Discarded,
    Failed
}

public enum CaptureMediaCompletionStatus
{
    Completed,
    CompletedWithWarnings,
    Interrupted,
    Discarded,
    Failed
}

public sealed record CaptureMediaStartRequest(
    long? AssessmentAttemptId,
    string SessionKey,
    string ModuleCode,
    string ModuleName,
    string CameraId);

public sealed record CaptureMediaSession(
    long SessionId,
    long? AssessmentAttemptId,
    string SessionKey,
    string ModuleCode,
    string ModuleName,
    string OutputDirectory,
    DateTimeOffset StartedAt);

public sealed record CaptureMediaCompleted(
    CaptureMediaSession Session,
    CaptureMediaCompletionStatus Status,
    string? ErrorCode,
    string? Message);

public interface ICaptureMediaService
{
    event EventHandler<CaptureMediaCompleted>? Completed;

    bool IsCapturing { get; }

    CaptureMediaSession? CurrentSession { get; }

    Task<CaptureMediaSession> StartAsync(
        CaptureMediaStartRequest request,
        CancellationToken cancellationToken = default);

    void RequestStop(
        CaptureMediaStopReason reason,
        string? message = null);

    Task StopAsync(
        CaptureMediaStopReason reason,
        string? message = null,
        CancellationToken cancellationToken = default);

    Task WaitForIdleAsync(CancellationToken cancellationToken = default);
}
