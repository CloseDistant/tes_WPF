namespace RuinaoSoftwareWpf;

using Microsoft.Data.Sqlite;
using System.IO;

/// <summary>
/// Creates all application SQLite connections with the product database key.
/// The fixed key intentionally allows a registered backup to be restored on
/// another workstation running the same software version.
/// </summary>
internal static class EncryptedSqliteDatabase
{
    // Product-controlled fixed key. Keep the released value under configuration control.
    internal const string ProductDatabaseKey =
        "7F3C90D1A8E2465BB42F1C8D93A5706E4D2B89F7631A0CE5B8D47F2196AC35E1";

    private const string CipherName = "sqlcipher";
    private const int CipherLegacyVersion = 4;

    public static string CreateConnectionString(
        string databasePath,
        SqliteOpenMode mode = SqliteOpenMode.ReadWriteCreate,
        bool pooling = true,
        SqliteCacheMode cache = SqliteCacheMode.Default,
        int defaultTimeout = 30)
    {
        var dataSource = CreateDatabaseUri(databasePath);
        return new SqliteConnectionStringBuilder
        {
            DataSource = dataSource,
            Password = ProductDatabaseKey,
            Mode = mode,
            Pooling = pooling,
            Cache = cache,
            DefaultTimeout = defaultTimeout
        }.ToString();
    }

    public static SqliteConnection CreateConnection(
        string databasePath,
        SqliteOpenMode mode = SqliteOpenMode.ReadWriteCreate,
        bool pooling = true,
        SqliteCacheMode cache = SqliteCacheMode.Default,
        int defaultTimeout = 30)
    {
        return new SqliteConnection(CreateConnectionString(databasePath, mode, pooling, cache, defaultTimeout));
    }

    public static string CreateDatabaseUri(string databasePath, bool includeKey = false)
    {
        var fileUri = new Uri(Path.GetFullPath(databasePath)).AbsoluteUri;
        var keyParameter = includeKey ? $"&key={ProductDatabaseKey}" : string.Empty;
        return $"{fileUri}?cipher={CipherName}&legacy={CipherLegacyVersion}{keyParameter}";
    }

    public static async Task PrepareEncryptedDatabaseAsync(
        string databasePath,
        ILoggingService logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(logger);

        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        if (!File.Exists(fullPath) || new FileInfo(fullPath).Length == 0)
        {
            return;
        }

        if (!HasPlaintextSqliteHeader(fullPath))
        {
            await VerifyCanOpenAsync(fullPath, cancellationToken).ConfigureAwait(false);
            CleanupAbandonedEncryptionFiles(fullPath, logger);
            return;
        }

        logger.Warning($"检测到历史未加密数据库，将删除并创建当前加密数据库：file={Path.GetFileName(fullPath)}");
        DeleteDatabaseFiles(fullPath);
        CleanupAbandonedEncryptionFiles(fullPath, logger);
    }

    public static void DeleteDatabaseFiles(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        SqliteConnection.ClearAllPools();
        DeleteIfExists(fullPath);
        DeleteIfExists(fullPath + "-wal");
        DeleteIfExists(fullPath + "-shm");
    }

    public static async Task VerifyCanOpenAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection(
            databasePath,
            SqliteOpenMode.ReadOnly,
            pooling: false);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool HasPlaintextSqliteHeader(string path)
    {
        Span<byte> header = stackalloc byte[16];
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        return stream.Read(header) == header.Length
            && header.SequenceEqual("SQLite format 3\0"u8);
    }

    private static void CleanupAbandonedEncryptionFiles(string databasePath, ILoggingService logger)
    {
        var directory = Path.GetDirectoryName(databasePath)!;
        var pattern = $"{Path.GetFileName(databasePath)}.*.encrypting";
        foreach (var path in Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly))
        {
            try
            {
                // An exclusive open prevents deleting a temporary database still used by another process.
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                }

                File.Delete(path);
                logger.Info($"已清理遗留数据库加密临时文件：file={Path.GetFileName(path)}");
            }
            catch (IOException)
            {
                // The file may still be in use; leave it for a later startup.
            }
            catch (UnauthorizedAccessException exception)
            {
                logger.Warning($"数据库加密临时文件清理失败：file={Path.GetFileName(path)}; reason={exception.Message}");
            }
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
