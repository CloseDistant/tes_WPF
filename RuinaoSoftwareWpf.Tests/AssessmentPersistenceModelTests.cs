namespace RuinaoSoftwareWpf.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Data.Sqlite;
using Xunit;

public sealed class AssessmentPersistenceModelTests
{
    [Fact]
    public void Model_BindsMarkerCodeAndAssessmentAttempt()
    {
        using var context = new CaptureDbContext("model-only.db", encrypted: false);

        var markerEntity = context.Model.FindEntityType(typeof(EegMarkerEntity));
        Assert.NotNull(markerEntity);
        var markerCode = markerEntity.FindProperty(nameof(EegMarkerEntity.MarkerCode));
        Assert.NotNull(markerCode);
        Assert.Equal(
            "marker_code",
            markerCode.GetColumnName(StoreObjectIdentifier.Table("eeg_markers", null)));
        Assert.True(markerCode.IsNullable);

        var moduleRecordEntity = context.Model.FindEntityType(typeof(AssessmentModuleRecordEntity));
        Assert.NotNull(moduleRecordEntity);
        var attemptProperty = moduleRecordEntity.FindProperty(
            nameof(AssessmentModuleRecordEntity.AssessmentAttemptId));
        Assert.NotNull(attemptProperty);
        Assert.Contains(
            moduleRecordEntity.GetIndexes(),
            index => index.IsUnique && index.Properties.SequenceEqual([attemptProperty]));

        var runEntity = context.Model.FindEntityType(typeof(AssessmentRunEntity));
        Assert.NotNull(runEntity?.FindProperty(nameof(AssessmentRunEntity.NextModuleTypeId)));

        var runModuleEntity = context.Model.FindEntityType(typeof(AssessmentRunModuleEntity));
        Assert.NotNull(runModuleEntity);
        Assert.NotNull(runModuleEntity.FindProperty(nameof(AssessmentRunModuleEntity.ModuleTypeId)));

        var attemptEntity = context.Model.FindEntityType(typeof(AssessmentModuleAttemptEntity));
        Assert.NotNull(attemptEntity?.FindProperty(nameof(AssessmentModuleAttemptEntity.ModuleTypeId)));
    }

    [Fact]
    public async Task Migration_CreatesAssessmentRunAndMarkerCodeSchema()
    {
        var testDirectory = Path.Combine(
            Path.GetTempPath(),
            "ruinao-assessment-schema-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        var databasePath = Path.Combine(testDirectory, "capture.db");

        try
        {
            await using (var context = new CaptureDbContext(databasePath, encrypted: false))
            {
                await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
            }

            await using (var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder
                {
                    DataSource = databasePath,
                    Pooling = false
                }.ToString()))
            {
                await connection.OpenAsync(TestContext.Current.CancellationToken);
                Assert.True(await TableExistsAsync(connection, "assessment_runs"));
                Assert.True(await TableExistsAsync(connection, "assessment_run_modules"));
                Assert.True(await TableExistsAsync(connection, "assessment_module_attempts"));
                Assert.True(await ColumnExistsAsync(connection, "assessment_runs", "next_module_type_id"));
                Assert.True(await ColumnExistsAsync(connection, "assessment_module_attempts", "module_type_id"));
                Assert.True(await ColumnExistsAsync(connection, "eeg_markers", "marker_code"));
                Assert.True(await ColumnExistsAsync(
                    connection,
                    "assessment_module_records",
                    "assessment_attempt_id"));

                await using var firstRun = connection.CreateCommand();
                firstRun.CommandText =
                    """
                    INSERT INTO assessment_runs
                        (patient_code, status, total_module_count, next_module_index,
                         started_at_unix_ms, created_at_unix_ms, updated_at_unix_ms)
                    VALUES
                        ('patient-a', 'in_progress', 22, 0, 1, 1, 1);
                    """;
                await firstRun.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

                await using var duplicateRun = connection.CreateCommand();
                duplicateRun.CommandText = firstRun.CommandText;
                await Assert.ThrowsAsync<SqliteException>(() =>
                    duplicateRun.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            }
        }
        finally
        {
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
    }

    private static async Task<bool> TableExistsAsync(SqliteConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt64(await command.ExecuteScalarAsync()) == 1;
    }

    private static async Task<bool> ColumnExistsAsync(
        SqliteConnection connection,
        string tableName,
        string columnName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName}\");";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
