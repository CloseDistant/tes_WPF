namespace RuinaoSoftwareWpf;

using Microsoft.EntityFrameworkCore;
using System.Text.Json;

internal sealed class SqliteReleaseIntegrityStateStore : IReleaseIntegrityStateStore
{
    private const string StateKey = "release_integrity_snapshot_v1";

    private readonly IAppDatabaseInitializer databaseInitializer;
    private readonly IAppDatabaseWriteCoordinator databaseWriteCoordinator;
    private readonly ILoggingService logger;
    private readonly string databasePath;
    private readonly bool encrypted;

    public SqliteReleaseIntegrityStateStore(
        IAppDatabaseInitializer databaseInitializer,
        IAppDatabaseWriteCoordinator databaseWriteCoordinator,
        ILoggingService logger)
        : this(
            databaseInitializer,
            databaseWriteCoordinator,
            logger,
            AppDatabasePathProvider.MainDatabasePath,
            encrypted: true)
    {
    }

    internal SqliteReleaseIntegrityStateStore(
        IAppDatabaseInitializer databaseInitializer,
        IAppDatabaseWriteCoordinator databaseWriteCoordinator,
        ILoggingService logger,
        string databasePath,
        bool encrypted)
    {
        this.databaseInitializer = databaseInitializer;
        this.databaseWriteCoordinator = databaseWriteCoordinator;
        this.logger = logger;
        this.databasePath = databasePath;
        this.encrypted = encrypted;
    }

    public async Task<ReleaseIntegritySnapshot?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await databaseInitializer.EnsureInitializedAsync(cancellationToken);
        await using var context = new CaptureDbContext(databasePath, encrypted);
        var value = await context.AppStates
            .AsNoTracking()
            .Where(item => item.Key == StateKey)
            .Select(item => item.Value)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ReleaseIntegritySnapshot>(value);
        }
        catch (JsonException exception)
        {
            logger.Warning($"发布文件校验状态无法解析，将按尚未校验处理：{exception.Message}");
            return null;
        }
    }

    public async Task SaveAsync(
        ReleaseIntegritySnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await databaseInitializer.EnsureInitializedAsync(cancellationToken);
        var value = JsonSerializer.Serialize(snapshot);
        await databaseWriteCoordinator.ExecuteAsync(
            databasePath,
            async () =>
            {
                await using var context = new CaptureDbContext(databasePath, encrypted);
                var state = await context.AppStates.FirstOrDefaultAsync(
                    item => item.Key == StateKey,
                    cancellationToken);
                if (state is null)
                {
                    state = new AppStateEntity { Key = StateKey };
                    context.AppStates.Add(state);
                }

                state.Value = value;
                state.UpdatedAtUnixMs = snapshot.CompletedAt.ToUnixTimeMilliseconds();
                await context.SaveChangesAsync(cancellationToken);
            },
            cancellationToken);
    }
}
