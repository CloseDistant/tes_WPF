namespace RuinaoSoftwareWpf;

public sealed record SessionLifecycleConfirmationRequest(
    string SessionKey,
    string Title,
    string Message,
    string ConfirmText,
    string CancelText,
    string CancelledResultMessage);

public sealed record SessionLifecycleResult(
    bool Succeeded,
    string Message,
    SessionLifecycleConfirmationRequest? Confirmation = null);

public interface ISessionLifecycleCoordinator
{
    event EventHandler? CurrentSessionChanged;

    UnifiedSessionContext? CurrentSession { get; }

    bool HasRunningModule { get; }

    Task<SessionLifecycleResult> EndCurrentAsync(
        string? confirmedSessionKey = null,
        CancellationToken cancellationToken = default);

    Task<SessionLifecycleResult> PrepareForPatientChangeAsync(
        string action,
        string? confirmedSessionKey = null,
        CancellationToken cancellationToken = default);

    Task InterruptForShutdownAsync(CancellationToken cancellationToken = default);
}
