namespace RuinaoSoftwareWpf;

/// <summary>
/// 三个实时模块共享的一次业务 Session。
/// UTC 用于跨文件查询，单调时钟用于同一进程内稳定排序和计算相对时间。
/// </summary>
public sealed record UnifiedSessionContext(
    string SessionKey,
    string PatientCode,
    DateTimeOffset StartedAtUtc,
    long OriginMonotonicTicks,
    long MonotonicFrequency);

public sealed record UnifiedSessionTimestamp(
    long EventTimeUnixMs,
    long SessionElapsedMs,
    long MonotonicTicks);

public sealed record UnifiedSessionTimelineEvent(
    string SessionKey,
    string ModuleCode,
    string EventType,
    long SequenceNo,
    long EventTimeUnixMs,
    long SessionElapsedMs,
    long MonotonicTicks,
    long MonotonicFrequency,
    long? SourceTimeUnixMs,
    string Message,
    string PayloadJson);

public static class SessionModuleCodes
{
    public const string Session = "session";
    public const string Stimulation = "stimulation";
    public const string Eeg = "eeg";
    public const string DigitalPhenotype = "digital_phenotype";
}

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

    Task EndAsync(
        string status,
        string? reason = null,
        CancellationToken cancellationToken = default);
}

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
