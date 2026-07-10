namespace RuinaoHardwareDebugWpf;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.IO;

public sealed class SqliteReportReadModelService : IReportReadModelService
{
    private readonly IAppDatabaseInitializer initializer;
    private readonly IAppDatabaseWriteCoordinator writeCoordinator;
    private static string SnapshotPath => AppDatabasePathProvider.MainDatabasePath + ".report-read";

    public SqliteReportReadModelService(
        IAppDatabaseInitializer initializer,
        IAppDatabaseWriteCoordinator writeCoordinator)
    {
        this.initializer = initializer;
        this.writeCoordinator = writeCoordinator;
    }

    public async Task RefreshSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await initializer.EnsureInitializedAsync(cancellationToken);
        var sourcePath = AppDatabasePathProvider.MainDatabasePath;
        await writeCoordinator.ExecuteAsync(sourcePath, async () =>
        {
            var temporaryPath = SnapshotPath + ".tmp";
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            await using var source = new SqliteConnection($"Data Source={sourcePath};Mode=ReadOnly");
            await using var target = new SqliteConnection($"Data Source={temporaryPath}");
            await source.OpenAsync(cancellationToken);
            await target.OpenAsync(cancellationToken);
            source.BackupDatabase(target);
            await target.CloseAsync();
            File.Move(temporaryPath, SnapshotPath, overwrite: true);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<SessionReportReadModel>> GetRecentSessionsAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SnapshotPath))
        {
            await RefreshSnapshotAsync(cancellationToken);
        }

        await using var context = new CaptureDbContext(SnapshotPath);
        return await context.AssessmentSessions
            .AsNoTracking()
            .OrderByDescending(item => item.StartedAtUnixMs)
            .Take(Math.Clamp(count, 1, 500))
            .Select(item => new SessionReportReadModel(
                item.SessionKey,
                item.PatientCode,
                item.Status,
                DateTimeOffset.FromUnixTimeMilliseconds(item.StartedAtUnixMs),
                item.EndedAtUnixMs == null ? null : DateTimeOffset.FromUnixTimeMilliseconds(item.EndedAtUnixMs.Value),
                context.SessionTimelineEvents.Count(eventItem => eventItem.SessionId == item.Id),
                context.AssessmentModuleRecords.Count(moduleItem => moduleItem.SessionId == item.Id)))
            .ToListAsync(cancellationToken);
    }
}
