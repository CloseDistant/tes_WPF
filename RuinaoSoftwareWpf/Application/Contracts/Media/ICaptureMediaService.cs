namespace RuinaoSoftwareWpf.ApplicationContracts;

public enum CaptureMediaStopReason
{
    Completed,
    Interrupted,
    Discarded,
    Failed
}

public sealed record CaptureMediaStartRequest(
    string SessionKey,
    string ModuleCode,
    string ModuleName,
    string CameraId);

public sealed record CaptureMediaSession(
    long SessionId,
    string SessionKey,
    string ModuleCode,
    string ModuleName,
    DateTimeOffset StartedAt);

public sealed record CaptureMediaCompleted(
    CaptureMediaSession Session,
    CaptureMediaStopReason Reason,
    string? Message);

public interface ICaptureMediaService
{
    event EventHandler<CaptureMediaCompleted>? Completed;

    bool IsCapturing { get; }

    CaptureMediaSession? CurrentSession { get; }

    Task<CaptureMediaSession> StartAsync(
        CaptureMediaStartRequest request,
        CancellationToken cancellationToken = default);

    Task StopAsync(
        CaptureMediaStopReason reason,
        string? message = null,
        CancellationToken cancellationToken = default);

    Task WaitForIdleAsync(CancellationToken cancellationToken = default);
}
