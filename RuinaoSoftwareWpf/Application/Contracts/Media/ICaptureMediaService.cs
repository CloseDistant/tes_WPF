namespace RuinaoSoftwareWpf.ApplicationContracts;

public interface ICaptureMediaService
{
    event EventHandler<CaptureMediaCompleted>? Completed;

    event EventHandler<CaptureAudioLevel>? AudioLevelAvailable;

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
