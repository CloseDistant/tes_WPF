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
    [Fact]
    public async Task EnsureInitializedAsync_MigratesLegacyRowsAcrossBatchBoundary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = Path.Combine(Path.GetTempPath(), $"ruinao-db-migration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var previousDirectory = Environment.GetEnvironmentVariable("RUINAO_DATA_DIRECTORY");
        Environment.SetEnvironmentVariable("RUINAO_DATA_DIRECTORY", directory);
        try
        {
            var databasePath = Path.Combine(directory, "ruinao_app.db");
            await CreateLegacyDatabaseAsync(databasePath, 501, cancellationToken);

            var initializer = new AppDatabaseInitializer(new TestLoggingService());
            await initializer.EnsureInitializedAsync(cancellationToken);

            await using var context = new CaptureDbContext(databasePath);
            Assert.Equal(501, await context.AppStates.CountAsync(cancellationToken));
            Assert.Equal("value-500", await context.AppStates
                .Where(item => item.Key == "legacy-500")
                .Select(item => item.Value)
                .SingleAsync(cancellationToken));
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

    private static async Task CreateLegacyDatabaseAsync(
        string databasePath,
        int rowCount,
        CancellationToken cancellationToken)
    {
        await using var context = new CaptureDbContext(databasePath, encrypted: false);
        await context.Database.EnsureCreatedAsync(cancellationToken);

        const int batchSize = 100;
        for (var offset = 0; offset < rowCount; offset += batchSize)
        {
            var count = Math.Min(batchSize, rowCount - offset);
            var rows = Enumerable.Range(offset, count)
                .Select(index => new AppStateEntity
                {
                    Key = $"legacy-{index}",
                    Value = $"value-{index}",
                    UpdatedAtUnixMs = index
                });
            await context.AppStates.AddRangeAsync(rows, cancellationToken);
            await context.SaveChangesAsync(cancellationToken);
            context.ChangeTracker.Clear();
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
