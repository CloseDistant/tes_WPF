namespace RuinaoSoftwareWpf;

using Microsoft.EntityFrameworkCore;

public sealed class LocalFeatureVisibilityService : IFeatureVisibilityService
{
    private readonly IAppDatabaseInitializer databaseInitializer;
    private readonly IAccountService accountService;
    private readonly ILoggingService logger;
    private readonly SemaphoreSlim initializeGate = new(1, 1);
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private readonly object stateGate = new();
    private readonly Dictionary<string, bool> visibility = new(StringComparer.Ordinal);
    private volatile bool initialized;

    public LocalFeatureVisibilityService(
        IAppDatabaseInitializer databaseInitializer,
        IAccountService accountService,
        ILoggingService logger)
    {
        this.databaseInitializer = databaseInitializer;
        this.accountService = accountService;
        this.logger = logger;
    }

    public event EventHandler? VisibilityChanged;

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
            var storedValues = await context.FeatureVisibilities
                .AsNoTracking()
                .Where(item => FeatureCatalog.AllKeys.Contains(item.FeatureKey))
                .ToDictionaryAsync(item => item.FeatureKey, item => item.IsVisible, cancellationToken);

            lock (stateGate)
            {
                visibility.Clear();
                foreach (var featureKey in FeatureCatalog.AllKeys)
                {
                    visibility[featureKey] = storedValues.GetValueOrDefault(
                        featureKey,
                        FeatureCatalog.DefaultVisibility(featureKey));
                }

                EnsureStimulationTypeAvailable(visibility);
            }

            initialized = true;
        }
        finally
        {
            initializeGate.Release();
        }
    }

    public bool IsVisible(string featureKey)
    {
        if (!FeatureCatalog.AllKeys.Contains(featureKey))
        {
            throw new ArgumentOutOfRangeException(nameof(featureKey), featureKey, "未知功能 Key");
        }

        lock (stateGate)
        {
            return visibility.GetValueOrDefault(featureKey, FeatureCatalog.DefaultVisibility(featureKey));
        }
    }

    public async Task SaveAsync(
        IReadOnlyDictionary<string, bool> updates,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var currentUser = accountService.CurrentUser;
        if (currentUser?.RoleId != AccountRoles.Admin)
        {
            await accountService.RecordAuditAsync(
                currentUser?.UserId,
                null,
                "update_feature_visibility",
                "failed",
                "非 Admin 账号尝试修改功能显示设置",
                cancellationToken);
            throw new InvalidOperationException("只有 Admin 可以修改功能显示设置");
        }

        if (updates.Count == 0 || updates.Keys.Any(key => !FeatureCatalog.AllKeys.Contains(key)))
        {
            throw new InvalidOperationException("功能显示设置包含未知或空的配置项");
        }

        Dictionary<string, bool> nextVisibility;
        lock (stateGate)
        {
            nextVisibility = new Dictionary<string, bool>(visibility, StringComparer.Ordinal);
        }

        foreach (var update in updates)
        {
            nextVisibility[update.Key] = update.Value;
        }

        EnsureStimulationTypeAvailable(nextVisibility);

        await writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var context = new CaptureDbContext(AppDatabasePathProvider.MainDatabasePath);
            var keys = updates.Keys.ToArray();
            var entities = await context.FeatureVisibilities
                .Where(item => keys.Contains(item.FeatureKey))
                .ToDictionaryAsync(item => item.FeatureKey, cancellationToken);
            var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();

            foreach (var update in updates)
            {
                if (!entities.TryGetValue(update.Key, out var entity))
                {
                    entity = new FeatureVisibilityEntity { FeatureKey = update.Key };
                    context.FeatureVisibilities.Add(entity);
                }

                entity.IsVisible = update.Value;
                entity.UpdatedByUserId = currentUser.UserId;
                entity.UpdatedAtUnixMs = now;
            }

            await context.SaveChangesAsync(cancellationToken);
            lock (stateGate)
            {
                visibility.Clear();
                foreach (var item in nextVisibility)
                {
                    visibility[item.Key] = item.Value;
                }
            }

            var changedKeys = string.Join(",", updates.Keys.OrderBy(key => key, StringComparer.Ordinal));
            await accountService.RecordAuditAsync(
                currentUser.UserId,
                null,
                "update_feature_visibility",
                "success",
                $"更新功能显示设置：{changedKeys}",
                cancellationToken);
            logger.Info($"功能显示设置已更新：operator={currentUser.UserId}, keys={changedKeys}");
        }
        finally
        {
            writeGate.Release();
        }

        VisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void EnsureStimulationTypeAvailable(IReadOnlyDictionary<string, bool> values)
    {
        if (FeatureCatalog.StimulationTypes.All(item => !values.GetValueOrDefault(item.Key, item.DefaultVisible)))
        {
            throw new InvalidOperationException("至少需要显示一种电刺激类型");
        }
    }
}
