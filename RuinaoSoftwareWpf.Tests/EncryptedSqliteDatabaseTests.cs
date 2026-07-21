namespace RuinaoSoftwareWpf.Tests;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

public sealed class EncryptedSqliteDatabaseTests
{
    [Fact]
    public async Task PrepareEncryptedDatabaseAsync_RemovesHistoricalPlaintextDatabase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = Path.Combine(Path.GetTempPath(), $"ruinao-encryption-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "business.db");
        try
        {
            await using (var plaintext = new ProbeDbContext(databasePath, encrypted: false))
            {
                await plaintext.Database.EnsureCreatedAsync(cancellationToken);
                plaintext.Items.Add(new ProbeEntity { Name = "preserved" });
                await plaintext.SaveChangesAsync(cancellationToken);
            }
            Assert.True(HasPlaintextSqliteHeader(databasePath));

            await EncryptedSqliteDatabase.PrepareEncryptedDatabaseAsync(
                databasePath,
                new NullLoggingService(),
                cancellationToken);
            Assert.False(File.Exists(databasePath));

            await using var encrypted = new ProbeDbContext(databasePath, encrypted: true);
            await encrypted.Database.EnsureCreatedAsync(cancellationToken);
            Assert.Empty(await encrypted.Items.ToListAsync(cancellationToken));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PrepareEncryptedDatabaseAsync_RemovesAbandonedEncryptionFileOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = Path.Combine(Path.GetTempPath(), $"ruinao-encryption-cleanup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "business.db");
        var abandonedPath = $"{databasePath}.{Guid.NewGuid():N}.encrypting";
        var backupPath = databasePath + ".before-reset.bak";
        try
        {
            await using (var context = new ProbeDbContext(databasePath, encrypted: true))
            {
                await context.Database.EnsureCreatedAsync(cancellationToken);
            }
            SqliteConnection.ClearAllPools();
            await File.WriteAllTextAsync(abandonedPath, "temporary", cancellationToken);
            await File.WriteAllTextAsync(backupPath, "preserve", cancellationToken);

            await EncryptedSqliteDatabase.PrepareEncryptedDatabaseAsync(
                databasePath,
                new NullLoggingService(),
                cancellationToken);

            Assert.False(File.Exists(abandonedPath));
            Assert.True(File.Exists(backupPath));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task EncryptedDatabase_WrongPasswordCannotReadSchema()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = Path.Combine(Path.GetTempPath(), $"ruinao-wrong-key-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "business.db");
        try
        {
            await using (var context = new ProbeDbContext(databasePath, encrypted: true))
            {
                await context.Database.EnsureCreatedAsync(cancellationToken);
            }
            SqliteConnection.ClearAllPools();

            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = EncryptedSqliteDatabase.CreateDatabaseUri(databasePath),
                Password = "incorrect-product-key",
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
            await Assert.ThrowsAsync<SqliteException>(() => connection.OpenAsync(cancellationToken));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PrepareEncryptedDatabaseAsync_DoesNotDeleteUnreadableDatabase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = Path.Combine(Path.GetTempPath(), $"ruinao-unreadable-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "business.db");
        var originalContent = "not-a-database"u8.ToArray();
        try
        {
            await File.WriteAllBytesAsync(databasePath, originalContent, cancellationToken);

            await Assert.ThrowsAnyAsync<Exception>(() =>
                EncryptedSqliteDatabase.PrepareEncryptedDatabaseAsync(
                    databasePath,
                    new NullLoggingService(),
                    cancellationToken));

            Assert.True(File.Exists(databasePath));
            Assert.Equal(originalContent, await File.ReadAllBytesAsync(databasePath, cancellationToken));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static bool HasPlaintextSqliteHeader(string path)
    {
        Span<byte> header = stackalloc byte[16];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return stream.Read(header) == header.Length && header.SequenceEqual("SQLite format 3\0"u8);
    }

    private sealed class ProbeDbContext : DbContext
    {
        private readonly string path;
        private readonly bool encrypted;

        public ProbeDbContext(string path, bool encrypted)
        {
            this.path = path;
            this.encrypted = encrypted;
        }

        public DbSet<ProbeEntity> Items => Set<ProbeEntity>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite(encrypted
                ? EncryptedSqliteDatabase.CreateConnectionString(path)
                : new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        }
    }

    private sealed class ProbeEntity
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class NullLoggingService : ILoggingService
    {
        public string CurrentLogPath => string.Empty;
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
        public void Hardware(string message) { }
        public void HardwareTx(string context, byte[] frame) { }
        public void HardwareRx(string context, byte[] frame) { }
        public void HardwareDecision(string message) { }
    }
}
