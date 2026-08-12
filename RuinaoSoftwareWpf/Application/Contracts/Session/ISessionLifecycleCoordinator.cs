namespace RuinaoSoftwareWpf;

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
