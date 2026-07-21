namespace RuinaoSoftwareWpf;

public sealed record SessionUnlockResult(
    bool Succeeded,
    bool IsBlocked,
    string Message);

public interface ISessionSecurityService
{
    const int MinimumIdleTimeoutMinutes = 5;
    const int MaximumIdleTimeoutMinutes = 30;
    const int DefaultIdleTimeoutMinutes = 15;

    event EventHandler? StateChanged;

    int IdleTimeoutMinutes { get; }

    DateTimeOffset LastActivityUtc { get; }

    DateTimeOffset? LockedAtUtc { get; }

    bool IsLocked { get; }

    bool IsAutoLockSuppressed { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    void NotifyUserActivity();

    Task EvaluateIdleTimeoutAsync(CancellationToken cancellationToken = default);

    Task<SessionUnlockResult> UnlockAsync(
        string password,
        CancellationToken cancellationToken = default);

    Task SaveIdleTimeoutAsync(
        int minutes,
        CancellationToken cancellationToken = default);
}
