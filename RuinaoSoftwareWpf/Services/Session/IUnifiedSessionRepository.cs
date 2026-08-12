namespace RuinaoSoftwareWpf;

public interface IUnifiedSessionRepository
{
    Task RecoverIncompleteSessionsAsync(long recoveredAtUnixMs, CancellationToken cancellationToken = default);

    Task EnsureSessionAsync(UnifiedSessionContext context, CancellationToken cancellationToken = default);

    Task RecordTimelineEventAsync(UnifiedSessionTimelineEvent timelineEvent, CancellationToken cancellationToken = default);

    Task<PageResult<UnifiedSessionTimelineEvent>> GetTimelinePageAsync(
        string sessionKey,
        PageRequest request,
        CancellationToken cancellationToken = default);

    Task CompleteUnifiedSessionAsync(
        string sessionKey,
        string status,
        long endedAtUnixMs,
        CancellationToken cancellationToken = default);
}
