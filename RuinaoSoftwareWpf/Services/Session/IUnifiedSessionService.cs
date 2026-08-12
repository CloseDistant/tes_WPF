namespace RuinaoSoftwareWpf;

public interface IUnifiedSessionService
{
    event EventHandler? CurrentSessionChanged;

    UnifiedSessionContext? CurrentSession { get; }

    Task<UnifiedSessionContext> GetOrStartAsync(CancellationToken cancellationToken = default);

    UnifiedSessionTimestamp GetCurrentTimestamp();

    Task<PageResult<UnifiedSessionTimelineEvent>> GetTimelinePageAsync(
        string sessionKey,
        PageRequest request,
        CancellationToken cancellationToken = default);

    Task RecordEventAsync(
        string moduleCode,
        string eventType,
        string? message = null,
        string? payloadJson = null,
        DateTimeOffset? sourceTime = null,
        CancellationToken cancellationToken = default);

    Task<bool> EndAsync(
        string status,
        string? reason = null,
        string? expectedSessionKey = null,
        CancellationToken cancellationToken = default);
}
