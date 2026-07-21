namespace RuinaoSoftwareWpf;

using Microsoft.EntityFrameworkCore;
using System.IO;

public interface IAppDatabaseInitializer
{
    Task EnsureInitializedAsync(CancellationToken cancellationToken = default);
}

public sealed class AppDatabaseInitializer : IAppDatabaseInitializer
{
    private const string InitialMigrationId = "202607100001_InitialSchema";

    private readonly ILoggingService logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool initialized;

    public AppDatabaseInitializer(ILoggingService logger)
    {
        this.logger = logger;
    }

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (initialized)
        {
            return;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (initialized)
            {
                return;
            }

            var databasePath = AppDatabasePathProvider.MainDatabasePath;
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            await EncryptedSqliteDatabase.PrepareEncryptedDatabaseAsync(
                databasePath,
                logger,
                cancellationToken);

            if (File.Exists(databasePath)
                && new FileInfo(databasePath).Length > 0
                && !await BelongsToCurrentMigrationLineageAsync(databasePath, cancellationToken))
            {
                logger.Warning("检测到不属于当前迁移体系的开发数据库，将删除并创建当前加密数据库。");
                EncryptedSqliteDatabase.DeleteDatabaseFiles(databasePath);
            }

            await using var context = new CaptureDbContext(databasePath);
            await context.Database.MigrateAsync(cancellationToken);
            DeleteObsoleteSecurityArtifact(Path.Combine(
                Path.GetDirectoryName(databasePath)!,
                "security",
                "business_data_hmac.key"));
            initialized = true;
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<bool> BelongsToCurrentMigrationLineageAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        await using var context = new CaptureDbContext(databasePath);
        var appliedMigrations = await context.Database.GetAppliedMigrationsAsync(cancellationToken);
        return appliedMigrations.Contains(InitialMigrationId, StringComparer.Ordinal);
    }

    private void DeleteObsoleteSecurityArtifact(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception)
        {
            logger.Warning($"旧安全文件清理失败：{exception.Message}");
        }
    }
}
