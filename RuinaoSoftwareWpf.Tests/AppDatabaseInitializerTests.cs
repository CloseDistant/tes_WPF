namespace RuinaoSoftwareWpf.Tests;

using Microsoft.EntityFrameworkCore;
using Xunit;

[CollectionDefinition(DatabaseEnvironmentCollection.Name, DisableParallelization = true)]
public sealed class DatabaseEnvironmentCollection
{
    public const string Name = "DatabaseEnvironment";
}

[Collection(DatabaseEnvironmentCollection.Name)]
public sealed class AppDatabaseInitializerTests
{
    private const string InitialMigrationId = "202607100001_InitialSchema";

    [Fact]
    public Task EnsureInitializedAsync_ReplacesHistoricalPlaintextDatabase() =>
        RunInIsolatedDataDirectoryAsync(async (databasePath, cancellationToken) =>
        {
            await using (var legacy = new CaptureDbContext(databasePath, encrypted: false))
            {
                await legacy.Database.EnsureCreatedAsync(cancellationToken);
                legacy.AppStates.Add(new AppStateEntity
                {
                    Key = "historical-plaintext",
                    Value = "discarded",
                    UpdatedAtUnixMs = 1
                });
                await legacy.SaveChangesAsync(cancellationToken);
            }

            var initializer = new AppDatabaseInitializer(new TestLoggingService());
            await initializer.EnsureInitializedAsync(cancellationToken);

            await using var current = new CaptureDbContext(databasePath);
            Assert.Contains(InitialMigrationId, await current.Database.GetAppliedMigrationsAsync(cancellationToken));
            Assert.False(await current.AppStates.AnyAsync(
                item => item.Key == "historical-plaintext",
                cancellationToken));
        });

    [Fact]
    public Task EnsureInitializedAsync_ReplacesEncryptedDatabaseWithoutMigrationLineage() =>
        RunInIsolatedDataDirectoryAsync(async (databasePath, cancellationToken) =>
        {
            await using (var unversioned = new CaptureDbContext(databasePath))
            {
                await unversioned.Database.EnsureCreatedAsync(cancellationToken);
                unversioned.AppStates.Add(new AppStateEntity
                {
                    Key = "unversioned",
                    Value = "discarded",
                    UpdatedAtUnixMs = 1
                });
                await unversioned.SaveChangesAsync(cancellationToken);
            }

            var initializer = new AppDatabaseInitializer(new TestLoggingService());
            await initializer.EnsureInitializedAsync(cancellationToken);

            await using var current = new CaptureDbContext(databasePath);
            Assert.Contains(InitialMigrationId, await current.Database.GetAppliedMigrationsAsync(cancellationToken));
            Assert.False(await current.AppStates.AnyAsync(
                item => item.Key == "unversioned",
                cancellationToken));
        });

    [Fact]
    public Task EnsureInitializedAsync_PreservesDatabaseFromCurrentMigrationLineage() =>
        RunInIsolatedDataDirectoryAsync(async (databasePath, cancellationToken) =>
        {
            var initialSetup = new AppDatabaseInitializer(new TestLoggingService());
            await initialSetup.EnsureInitializedAsync(cancellationToken);

            await using (var existing = new CaptureDbContext(databasePath))
            {
                existing.AppStates.Add(new AppStateEntity
                {
                    Key = "current-lineage",
                    Value = "preserved",
                    UpdatedAtUnixMs = 1
                });
                await existing.SaveChangesAsync(cancellationToken);
            }

            var nextStartup = new AppDatabaseInitializer(new TestLoggingService());
            await nextStartup.EnsureInitializedAsync(cancellationToken);

            await using var current = new CaptureDbContext(databasePath);
            Assert.Equal("preserved", await current.AppStates
                .Where(item => item.Key == "current-lineage")
                .Select(item => item.Value)
                .SingleAsync(cancellationToken));
            Assert.Empty(await current.Database.GetPendingMigrationsAsync(cancellationToken));
        });

    private static async Task RunInIsolatedDataDirectoryAsync(
        Func<string, CancellationToken, Task> test)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = Path.Combine(Path.GetTempPath(), $"ruinao-db-initializer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var previousDirectory = Environment.GetEnvironmentVariable("RUINAO_DATA_DIRECTORY");
        Environment.SetEnvironmentVariable("RUINAO_DATA_DIRECTORY", directory);
        try
        {
            await test(Path.Combine(directory, "ruinao_app.db"), cancellationToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RUINAO_DATA_DIRECTORY", previousDirectory);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class TestLoggingService : ILoggingService
    {
        public string CurrentLogPath => string.Empty;
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
        public void Hardware(string message) { }
        public void HardwareTx(string command, byte[] frame) { }
        public void HardwareRx(string source, byte[] frame) { }
        public void HardwareDecision(string message) { }
    }
}
