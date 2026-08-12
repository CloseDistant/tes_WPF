namespace RuinaoSoftwareWpf;

using Microsoft.EntityFrameworkCore;

public sealed class SessionSecurityService : ISessionSecurityService
{
    private const string IdleTimeoutKey = "session_idle_timeout_minutes";

    private readonly IAppDatabaseInitializer databaseInitializer;
    private readonly IAppDatabaseWriteCoordinator databaseWriteCoordinator;
    private readonly IAccountService accountService;
    private readonly ISessionLifecycleCoordinator sessionLifecycleCoordinator;
    private readonly IAssessmentActivityState assessmentActivityState;
    private readonly ILoggingService logger;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim initializeGate = new(1, 1);
    private readonly SemaphoreSlim evaluationGate = new(1, 1);
    private readonly SemaphoreSlim settingsWriteGate = new(1, 1);

    private int initialized;
    private int idleTimeoutMinutes = ISessionSecurityService.DefaultIdleTimeoutMinutes;
    private int isLocked;
    private int isAutoLockSuppressed;
    private long lastActivityTimestamp;
    private long lastActivityUnixMs;
    private long lockedAtUnixMs;

    public SessionSecurityService(
        IAppDatabaseInitializer databaseInitializer,
        IAppDatabaseWriteCoordinator databaseWriteCoordinator,
        IAccountService accountService,
        ISessionLifecycleCoordinator sessionLifecycleCoordinator,
        IAssessmentActivityState assessmentActivityState,
        ILoggingService logger,
        TimeProvider timeProvider)
    {
        this.databaseInitializer = databaseInitializer;
        this.databaseWriteCoordinator = databaseWriteCoordinator;
        this.accountService = accountService;
        this.sessionLifecycleCoordinator = sessionLifecycleCoordinator;
        this.assessmentActivityState = assessmentActivityState;
        this.logger = logger;
        this.timeProvider = timeProvider;

        ResetActivityTimestamp();
        accountService.CurrentUserChanged += OnCurrentUserChanged;
    }

    public event EventHandler? StateChanged;

    public int IdleTimeoutMinutes => Volatile.Read(ref idleTimeoutMinutes);

    public DateTimeOffset LastActivityUtc => DateTimeOffset.FromUnixTimeMilliseconds(
        Interlocked.Read(ref lastActivityUnixMs));

    public DateTimeOffset? LockedAtUtc
    {
        get
        {
            var value = Interlocked.Read(ref lockedAtUnixMs);
            return value == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(value);
        }
    }

    public bool IsLocked => Volatile.Read(ref isLocked) == 1;

    public bool IsAutoLockSuppressed => Volatile.Read(ref isAutoLockSuppressed) == 1;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref initialized) == 1)
        {
            return;
        }

        await initializeGate.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref initialized) == 1)
            {
                return;
            }

            await databaseInitializer.EnsureInitializedAsync(cancellationToken);
            await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
            var storedValue = await context.AppStates
                .AsNoTracking()
                .Where(item => item.Key == IdleTimeoutKey)
                .Select(item => item.Value)
                .FirstOrDefaultAsync(cancellationToken);

            var loadedValue = SessionSecurityPolicy.ParseIdleTimeoutOrDefault(storedValue);
            Volatile.Write(ref idleTimeoutMinutes, loadedValue);
            ResetActivityTimestamp();
            Volatile.Write(ref initialized, 1);
            logger.Info($"会话安全设置已加载：idleTimeoutMinutes={loadedValue}");
        }
        finally
        {
            initializeGate.Release();
        }
    }

    public void NotifyUserActivity()
    {
        if (accountService.CurrentUser is null || IsLocked)
        {
            return;
        }

        ResetActivityTimestamp();
    }

    public async Task EvaluateIdleTimeoutAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        if (accountService.CurrentUser is null || IsLocked)
        {
            return;
        }

        await evaluationGate.WaitAsync(cancellationToken);
        try
        {
            if (accountService.CurrentUser is null || IsLocked)
            {
                return;
            }

            var shouldSuppress = SessionSecurityPolicy.ShouldSuppressAutoLock(
                sessionLifecycleCoordinator.HasRunningModule,
                assessmentActivityState.IsActiveForSessionSecurity);
            if (shouldSuppress)
            {
                var changed = Interlocked.Exchange(ref isAutoLockSuppressed, 1) == 0;
                ResetActivityTimestamp();
                if (changed)
                {
                    StateChanged?.Invoke(this, EventArgs.Empty);
                }

                return;
            }

            if (Interlocked.Exchange(ref isAutoLockSuppressed, 0) == 1)
            {
                ResetActivityTimestamp();
                StateChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            var elapsed = timeProvider.GetElapsedTime(Interlocked.Read(ref lastActivityTimestamp));
            if (!SessionSecurityPolicy.HasIdleTimeoutElapsed(elapsed, IdleTimeoutMinutes)
                || Interlocked.CompareExchange(ref isLocked, 1, 0) != 0)
            {
                return;
            }

            var user = accountService.CurrentUser;
            var lockedAt = timeProvider.GetUtcNow();
            Interlocked.Exchange(ref lockedAtUnixMs, lockedAt.ToUnixTimeMilliseconds());
            StateChanged?.Invoke(this, EventArgs.Empty);

            var message = $"连续无操作达到 {IdleTimeoutMinutes} 分钟，当前会话已锁定";
            logger.Info($"会话超时锁定：userId={user?.UserId}, idleTimeoutMinutes={IdleTimeoutMinutes}");
            await TryRecordAuditAsync(user, "session_timeout", "success", message, cancellationToken);
            await TryRecordAuditAsync(user, "session_lock", "success", "锁定原因：idle_timeout", cancellationToken);
        }
        finally
        {
            evaluationGate.Release();
        }
    }

    public async Task<SessionUnlockResult> UnlockAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        if (!IsLocked)
        {
            return new SessionUnlockResult(true, false, "当前会话未锁定");
        }

        var verification = await accountService.VerifyCurrentPasswordAsync(password, cancellationToken);
        if (!verification.Succeeded)
        {
            logger.Warning(
                $"会话解锁失败：userId={accountService.CurrentUser?.UserId}, blocked={verification.IsBlocked}");
            return new SessionUnlockResult(false, verification.IsBlocked, verification.Message);
        }

        Interlocked.Exchange(ref isLocked, 0);
        Interlocked.Exchange(ref lockedAtUnixMs, 0);
        ResetActivityTimestamp();
        StateChanged?.Invoke(this, EventArgs.Empty);
        logger.Info($"会话解锁成功：userId={accountService.CurrentUser?.UserId}");
        return new SessionUnlockResult(true, false, verification.Message);
    }

    public async Task SaveIdleTimeoutAsync(
        int minutes,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var user = accountService.CurrentUser;
        if (user?.RoleId != AccountRoles.Admin)
        {
            await TryRecordAuditAsync(
                user,
                "update_session_security_settings",
                "failed",
                "非Admin账号尝试修改自动锁定时间",
                cancellationToken);
            throw new InvalidOperationException("只有Admin可以修改自动锁定时间");
        }

        if (!SessionSecurityPolicy.IsValidIdleTimeout(minutes))
        {
            await TryRecordAuditAsync(
                user,
                "update_session_security_settings",
                "failed",
                $"自动锁定时间超出范围：{minutes}",
                cancellationToken);
            throw new ArgumentOutOfRangeException(
                nameof(minutes),
                $"自动锁定时间必须为{ISessionSecurityService.MinimumIdleTimeoutMinutes}至{ISessionSecurityService.MaximumIdleTimeoutMinutes}分钟");
        }

        await settingsWriteGate.WaitAsync(cancellationToken);
        try
        {
            var previous = IdleTimeoutMinutes;
            await databaseWriteCoordinator.ExecuteAsync(
                AppDatabasePathProvider.MainDatabasePath,
                async () =>
                {
                    await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
                    var state = await context.AppStates.FirstOrDefaultAsync(
                        item => item.Key == IdleTimeoutKey,
                        cancellationToken);
                    if (state is null)
                    {
                        state = new AppStateEntity { Key = IdleTimeoutKey };
                        context.AppStates.Add(state);
                    }

                    state.Value = minutes.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    state.UpdatedAtUnixMs = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
                    await context.SaveChangesAsync(cancellationToken);
                },
                cancellationToken);

            Volatile.Write(ref idleTimeoutMinutes, minutes);
            ResetActivityTimestamp();
            StateChanged?.Invoke(this, EventArgs.Empty);
            await TryRecordAuditAsync(
                user,
                "update_session_security_settings",
                "success",
                $"自动锁定时间：{previous} -> {minutes} 分钟",
                cancellationToken);
            logger.Info($"会话安全设置已更新：operator={user.UserId}, idleTimeoutMinutes={minutes}");
        }
        catch (Exception exception)
        {
            await TryRecordAuditAsync(
                user,
                "update_session_security_settings",
                "failed",
                $"保存自动锁定时间失败：{exception.Message}",
                cancellationToken);
            throw;
        }
        finally
        {
            settingsWriteGate.Release();
        }
    }

    private void ResetActivityTimestamp()
    {
        Interlocked.Exchange(ref lastActivityTimestamp, timeProvider.GetTimestamp());
        Interlocked.Exchange(ref lastActivityUnixMs, timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
    }

    private void OnCurrentUserChanged(object? sender, EventArgs e)
    {
        Interlocked.Exchange(ref isLocked, 0);
        Interlocked.Exchange(ref isAutoLockSuppressed, 0);
        Interlocked.Exchange(ref lockedAtUnixMs, 0);
        ResetActivityTimestamp();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task TryRecordAuditAsync(
        CurrentUserInfo? user,
        string action,
        string result,
        string message,
        CancellationToken cancellationToken)
    {
        try
        {
            await accountService.RecordAuditAsync(
                user?.UserId,
                user?.UserId,
                action,
                result,
                message,
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.Error($"会话安全审计写入失败：action={action}, result={result}", exception);
        }
    }
}
