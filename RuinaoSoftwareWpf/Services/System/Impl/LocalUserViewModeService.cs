namespace RuinaoSoftwareWpf;

using Microsoft.EntityFrameworkCore;

public sealed class LocalUserViewModeService : IUserViewModeService
{
    private const string UserViewModeKey = "user_view_mode";
    private readonly IAppDatabaseInitializer databaseInitializer;
    private readonly IAppDatabaseWriteCoordinator databaseWriteCoordinator;
    private readonly ILoggingService logger;
    private readonly SemaphoreSlim initializeGate = new(1, 1);
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private volatile bool initialized;
    private volatile UserViewMode currentMode = UserViewMode.Operator;

    public LocalUserViewModeService(
        IAppDatabaseInitializer databaseInitializer,
        IAppDatabaseWriteCoordinator databaseWriteCoordinator,
        ILoggingService logger)
    {
        this.databaseInitializer = databaseInitializer;
        this.databaseWriteCoordinator = databaseWriteCoordinator;
        this.logger = logger;
    }

    public UserViewMode CurrentMode => currentMode;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (initialized)
        {
            return;
        }

        await initializeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (initialized)
            {
                return;
            }

            await databaseInitializer.EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
            var storedValue = await context.AppStates
                .AsNoTracking()
                .Where(item => item.Key == UserViewModeKey)
                .Select(item => item.Value)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            currentMode = Parse(storedValue);
            initialized = true;
            logger.Info($"用户视角已加载：{currentMode}");
        }
        finally
        {
            initializeGate.Release();
        }
    }

    public async Task SetModeAsync(UserViewMode mode, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
            var state = await context.AppStates.FirstOrDefaultAsync(
                item => item.Key == UserViewModeKey,
                cancellationToken).ConfigureAwait(false);
            if (state is null)
            {
                state = new AppStateEntity { Key = UserViewModeKey };
                context.AppStates.Add(state);
            }

            state.Value = mode == UserViewMode.Patient ? "patient" : "operator";
            state.UpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await databaseWriteCoordinator.ExecuteAsync(
                AppDatabasePathProvider.MainDatabasePath,
                () => context.SaveChangesAsync(cancellationToken),
                cancellationToken).ConfigureAwait(false);

            currentMode = mode;
            logger.Info($"用户视角已保存：{currentMode}");
        }
        finally
        {
            writeGate.Release();
        }
    }

    private static UserViewMode Parse(string? value) =>
        string.Equals(value?.Trim(), "patient", StringComparison.OrdinalIgnoreCase)
            ? UserViewMode.Patient
            : UserViewMode.Operator;
}
