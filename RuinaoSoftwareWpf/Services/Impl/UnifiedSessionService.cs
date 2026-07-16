namespace RuinaoSoftwareWpf;

using System.Diagnostics;

/// <summary>
/// 应用级 Session 协调器。第一次实时模块启动时自动创建，直到用户结束、切换患者或退出软件。
/// </summary>
public sealed class UnifiedSessionService : IUnifiedSessionService
{
    private readonly IPatientService patientService;
    private readonly IUnifiedSessionRepository repository;
    private readonly ILoggingService logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private UnifiedSessionContext? currentSession;
    private long nextSequenceNo;
    private bool recoveryCompleted;

    public UnifiedSessionService(
        IPatientService patientService,
        IUnifiedSessionRepository repository,
        ILoggingService logger)
    {
        this.patientService = patientService;
        this.repository = repository;
        this.logger = logger;
    }

    public event EventHandler? CurrentSessionChanged;

    public UnifiedSessionContext? CurrentSession => currentSession;

    public async Task<UnifiedSessionContext> GetOrStartAsync(CancellationToken cancellationToken = default)
    {
        var notifyChanged = false;
        UnifiedSessionContext result;
        await gate.WaitAsync(cancellationToken);
        try
        {
            (result, notifyChanged) = await EnsureStartedCoreAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }

        if (notifyChanged)
        {
            CurrentSessionChanged?.Invoke(this, EventArgs.Empty);
        }

        return result;
    }

    public UnifiedSessionTimestamp GetCurrentTimestamp()
    {
        var session = currentSession ?? throw new InvalidOperationException("当前没有活动 Session。");
        return CreateTimestamp(session);
    }

    public Task<IReadOnlyList<UnifiedSessionTimelineEvent>> GetTimelineAsync(
        string sessionKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionKey))
        {
            throw new ArgumentException("SessionKey 不能为空。", nameof(sessionKey));
        }

        return repository.GetTimelineAsync(sessionKey.Trim(), cancellationToken);
    }

    public async Task RecordEventAsync(
        string moduleCode,
        string eventType,
        string? message = null,
        string? payloadJson = null,
        DateTimeOffset? sourceTime = null,
        CancellationToken cancellationToken = default)
    {
        var notifyChanged = false;
        await gate.WaitAsync(cancellationToken);
        try
        {
            var (session, created) = await EnsureStartedCoreAsync(cancellationToken);
            notifyChanged = created;
            var timestamp = CreateTimestamp(session);
            await repository.RecordTimelineEventAsync(
                new UnifiedSessionTimelineEvent(
                    session.SessionKey,
                    moduleCode,
                    eventType,
                    ++nextSequenceNo,
                    timestamp.EventTimeUnixMs,
                    timestamp.SessionElapsedMs,
                    timestamp.MonotonicTicks,
                    session.MonotonicFrequency,
                    sourceTime?.ToUnixTimeMilliseconds(),
                    message ?? string.Empty,
                    payloadJson ?? string.Empty),
                cancellationToken);
        }
        finally
        {
            gate.Release();
        }

        if (notifyChanged)
        {
            CurrentSessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task EndAsync(
        string status,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var ended = false;
        await gate.WaitAsync(cancellationToken);
        try
        {
            var session = currentSession;
            if (session is null)
            {
                return;
            }

            var timestamp = CreateTimestamp(session);
            await repository.RecordTimelineEventAsync(
                new UnifiedSessionTimelineEvent(
                    session.SessionKey,
                    SessionModuleCodes.Session,
                    "session_end",
                    ++nextSequenceNo,
                    timestamp.EventTimeUnixMs,
                    timestamp.SessionElapsedMs,
                    timestamp.MonotonicTicks,
                    session.MonotonicFrequency,
                    null,
                    reason ?? status,
                    string.Empty),
                cancellationToken);
            await repository.CompleteUnifiedSessionAsync(
                session.SessionKey,
                status,
                timestamp.EventTimeUnixMs,
                cancellationToken);

            logger.Info($"统一 Session 已结束：session={session.SessionKey}, status={status}, elapsedMs={timestamp.SessionElapsedMs}");
            currentSession = null;
            nextSequenceNo = 0;
            ended = true;
        }
        finally
        {
            gate.Release();
        }

        if (ended)
        {
            CurrentSessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<(UnifiedSessionContext Session, bool Created)> EnsureStartedCoreAsync(CancellationToken cancellationToken)
    {
        if (currentSession is not null)
        {
            var currentPatientCode = patientService.CurrentPatient?.PatientCode;
            if (!string.IsNullOrWhiteSpace(currentPatientCode)
                && !string.Equals(currentPatientCode, currentSession.PatientCode, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("当前患者与活动 Session 不一致，请先结束 Session。");
            }

            return (currentSession, false);
        }

        var patientCode = await patientService.GetRequiredCurrentPatientCodeAsync(cancellationToken);
        var startedAtUtc = DateTimeOffset.UtcNow;
        if (!recoveryCompleted)
        {
            await repository.RecoverIncompleteSessionsAsync(startedAtUtc.ToUnixTimeMilliseconds(), cancellationToken);
            recoveryCompleted = true;
        }

        var baseKey = CaptureOutputPathProvider.CreateSessionKey(startedAtUtc);
        var session = new UnifiedSessionContext(
            $"{baseKey}_{Guid.NewGuid():N}"[..(baseKey.Length + 9)],
            patientCode,
            startedAtUtc,
            Stopwatch.GetTimestamp(),
            Stopwatch.Frequency);
        await repository.EnsureSessionAsync(session, cancellationToken);

        var timestamp = CreateTimestamp(session);
        try
        {
            await repository.RecordTimelineEventAsync(
                new UnifiedSessionTimelineEvent(
                    session.SessionKey,
                    SessionModuleCodes.Session,
                    "session_start",
                    1,
                    timestamp.EventTimeUnixMs,
                    timestamp.SessionElapsedMs,
                    timestamp.MonotonicTicks,
                    session.MonotonicFrequency,
                    null,
                    "统一 Session 开始",
                    string.Empty),
                cancellationToken);
        }
        catch
        {
            await repository.CompleteUnifiedSessionAsync(
                session.SessionKey,
                "start_failed",
                timestamp.EventTimeUnixMs,
                CancellationToken.None);
            throw;
        }

        currentSession = session;
        nextSequenceNo = 1;

        logger.Info($"统一 Session 已开始：session={session.SessionKey}, patient={session.PatientCode}, monotonicFrequency={session.MonotonicFrequency}");
        return (session, true);
    }

    private static UnifiedSessionTimestamp CreateTimestamp(UnifiedSessionContext session)
    {
        var monotonicTicks = Stopwatch.GetTimestamp();
        var elapsedTicks = Math.Max(0, monotonicTicks - session.OriginMonotonicTicks);
        var elapsedMs = (long)(elapsedTicks * 1000d / session.MonotonicFrequency);
        var eventTime = session.StartedAtUtc.AddMilliseconds(elapsedMs).ToUnixTimeMilliseconds();
        return new UnifiedSessionTimestamp(eventTime, elapsedMs, monotonicTicks);
    }
}
