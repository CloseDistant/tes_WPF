namespace RuinaoSoftwareWpf;

public sealed record SessionLifecycleConfirmationRequest(
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
        bool confirmed = false,
        CancellationToken cancellationToken = default);

    Task<SessionLifecycleResult> PrepareForPatientChangeAsync(
        string action,
        bool confirmed = false,
        CancellationToken cancellationToken = default);

    Task InterruptForShutdownAsync(CancellationToken cancellationToken = default);
}
