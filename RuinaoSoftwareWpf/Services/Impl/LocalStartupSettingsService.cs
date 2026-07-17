namespace RuinaoSoftwareWpf;

using Microsoft.EntityFrameworkCore;

public sealed class LocalStartupSettingsService : IStartupSettingsService
{
    private const string AutoConnectOnStartupKey = "auto_connect_on_startup";

    private readonly IAppDatabaseInitializer databaseInitializer;
    private readonly IAccountService accountService;
    private readonly ILoggingService logger;
    private readonly SemaphoreSlim initializeGate = new(1, 1);
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private volatile bool initialized;
    private volatile bool autoConnectOnStartup;

    public LocalStartupSettingsService(
        IAppDatabaseInitializer databaseInitializer,
        IAccountService accountService,
        ILoggingService logger)
    {
        this.databaseInitializer = databaseInitializer;
        this.accountService = accountService;
        this.logger = logger;
    }

    public bool AutoConnectOnStartup => autoConnectOnStartup;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (initialized)
        {
            return;
        }

        await initializeGate.WaitAsync(cancellationToken);
        try
        {
            if (initialized)
            {
                return;
            }

            await databaseInitializer.EnsureInitializedAsync(cancellationToken);
            await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
            var storedValue = await context.AppStates
                .AsNoTracking()
                .Where(item => item.Key == AutoConnectOnStartupKey)
                .Select(item => item.Value)
                .FirstOrDefaultAsync(cancellationToken);

            autoConnectOnStartup = bool.TryParse(storedValue, out var enabled) && enabled;
            initialized = true;
            logger.Info($"启动设置已加载：autoConnectOnStartup={autoConnectOnStartup}");
        }
        finally
        {
            initializeGate.Release();
        }
    }

    public async Task SaveAutoConnectOnStartupAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var currentUser = accountService.CurrentUser;

        await writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
            var state = await context.AppStates.FirstOrDefaultAsync(
                item => item.Key == AutoConnectOnStartupKey,
                cancellationToken);
            var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            if (state is null)
            {
                state = new AppStateEntity
                {
                    Key = AutoConnectOnStartupKey
                };
                context.AppStates.Add(state);
            }

            state.Value = enabled.ToString();
            state.UpdatedAtUnixMs = now;
            await context.SaveChangesAsync(cancellationToken);
            autoConnectOnStartup = enabled;

            await accountService.RecordAuditAsync(
                currentUser?.UserId,
                null,
                "update_startup_settings",
                "success",
                $"启动时自动联机：{enabled}",
                cancellationToken);
            logger.Info(
                $"启动设置已更新：operator={currentUser?.UserId.ToString() ?? "none"}, autoConnectOnStartup={enabled}");
        }
        finally
        {
            writeGate.Release();
        }
    }
}
