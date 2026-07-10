namespace RuinaoHardwareDebugWpf;

public sealed record SessionLifecycleResult(bool Succeeded, string Message);

public interface ISessionLifecycleCoordinator
{
    event EventHandler? CurrentSessionChanged;

    UnifiedSessionContext? CurrentSession { get; }

    bool HasRunningModule { get; }

    Task<SessionLifecycleResult> EndCurrentAsync(CancellationToken cancellationToken = default);

    Task<SessionLifecycleResult> PrepareForPatientChangeAsync(
        string action,
        CancellationToken cancellationToken = default);

    Task InterruptForShutdownAsync(CancellationToken cancellationToken = default);
}
