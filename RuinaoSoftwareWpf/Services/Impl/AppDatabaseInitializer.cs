namespace RuinaoSoftwareWpf;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.IO;

public interface IAppDatabaseInitializer
{
    Task EnsureInitializedAsync(CancellationToken cancellationToken = default);
}

public sealed class AppDatabaseInitializer : IAppDatabaseInitializer
{
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
            if (File.Exists(databasePath) && new FileInfo(databasePath).Length > 0)
            {
                await UpgradeLegacyDatabaseIfRequiredAsync(databasePath, cancellationToken);
            }

            await using var context = new CaptureDbContext(databasePath);
            await context.Database.MigrateAsync(cancellationToken);
            initialized = true;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task UpgradeLegacyDatabaseIfRequiredAsync(string databasePath, CancellationToken cancellationToken)
    {
        await using (var probe = new CaptureDbContext(databasePath))
        {
            if ((await probe.Database.GetAppliedMigrationsAsync(cancellationToken)).Any())
            {
                return;
            }
        }

        var temporaryPath = databasePath + ".migrating";
        if (File.Exists(temporaryPath))
        {
            File.Delete(temporaryPath);
        }

        logger.Info("检测到旧版数据库，开始通过 EF Core 实体迁移到正式版本结构。");
        var snapshot = await LegacyDatabaseSnapshot.LoadAsync(databasePath, logger, cancellationToken);
        await using (var target = new CaptureDbContext(temporaryPath))
        {
            await target.Database.MigrateAsync(cancellationToken);
            snapshot.AddTo(target);
            await target.SaveChangesAsync(cancellationToken);
        }

        SqliteConnection.ClearAllPools();
        var backupPath = databasePath + $".legacy-{DateTime.Now:yyyyMMddHHmmss}.bak";
        File.Move(databasePath, backupPath);
        File.Move(temporaryPath, databasePath);
        logger.Info($"旧版数据库升级完成，原数据库已备份为：{Path.GetFileName(backupPath)}");
    }

    private sealed class LegacyDatabaseSnapshot
    {
        public List<AccountUserEntity> Users { get; } = [];
        public List<AccountAuditLogEntity> AccountAuditLogs { get; } = [];
        public List<PatientEntity> Patients { get; } = [];
        public List<AppStateEntity> AppStates { get; } = [];
        public List<FeatureVisibilityEntity> FeatureVisibilities { get; } = [];
        public List<StimulationRecordEntity> StimulationRecords { get; } = [];
        public List<AssessmentSessionEntity> AssessmentSessions { get; } = [];
        public List<AssessmentModuleRecordEntity> AssessmentModuleRecords { get; } = [];
        public List<AssessmentEventEntity> AssessmentEvents { get; } = [];
        public List<SensorSampleEntity> SensorSamples { get; } = [];
        public List<EegRecordingEntity> EegRecordings { get; } = [];
        public List<EegDataSegmentEntity> EegDataSegments { get; } = [];
        public List<EegMarkerEntity> EegMarkers { get; } = [];

        public static async Task<LegacyDatabaseSnapshot> LoadAsync(
            string databasePath,
            ILoggingService logger,
            CancellationToken cancellationToken)
        {
            var result = new LegacyDatabaseSnapshot();
            await using var source = new CaptureDbContext(databasePath);
            await LoadOptionalAsync(source.Users, result.Users, "users", logger, cancellationToken);
            await LoadOptionalAsync(source.AccountAuditLogs, result.AccountAuditLogs, "account_audit_logs", logger, cancellationToken);
            await LoadOptionalAsync(source.Patients, result.Patients, "patients", logger, cancellationToken);
            await LoadOptionalAsync(source.AppStates, result.AppStates, "app_state", logger, cancellationToken);
            await LoadOptionalAsync(source.FeatureVisibilities, result.FeatureVisibilities, "feature_visibility", logger, cancellationToken);
            await LoadOptionalAsync(source.StimulationRecords, result.StimulationRecords, "stimulation_records", logger, cancellationToken);
            await LoadOptionalAsync(source.AssessmentSessions, result.AssessmentSessions, "assessment_sessions", logger, cancellationToken);
            await LoadOptionalAsync(source.AssessmentModuleRecords, result.AssessmentModuleRecords, "assessment_module_records", logger, cancellationToken);
            await LoadOptionalAsync(source.AssessmentEvents, result.AssessmentEvents, "assessment_events", logger, cancellationToken);
            await LoadOptionalAsync(source.SensorSamples, result.SensorSamples, "sensor_samples", logger, cancellationToken);
            await LoadOptionalAsync(source.EegRecordings, result.EegRecordings, "eeg_recordings", logger, cancellationToken);
            await LoadOptionalAsync(source.EegDataSegments, result.EegDataSegments, "eeg_data_segments", logger, cancellationToken);
            await LoadOptionalAsync(source.EegMarkers, result.EegMarkers, "eeg_markers", logger, cancellationToken);
            return result;
        }

        public void AddTo(CaptureDbContext context)
        {
            context.Users.AddRange(Users);
            context.AccountAuditLogs.AddRange(AccountAuditLogs);
            context.Patients.AddRange(Patients);
            context.AppStates.AddRange(AppStates);
            context.FeatureVisibilities.AddRange(FeatureVisibilities);
            context.StimulationRecords.AddRange(StimulationRecords);
            context.AssessmentSessions.AddRange(AssessmentSessions);
            context.AssessmentModuleRecords.AddRange(AssessmentModuleRecords);
            context.AssessmentEvents.AddRange(AssessmentEvents);
            context.SensorSamples.AddRange(SensorSamples);
            context.EegRecordings.AddRange(EegRecordings);
            context.EegDataSegments.AddRange(EegDataSegments);
            context.EegMarkers.AddRange(EegMarkers);
        }

        private static async Task LoadOptionalAsync<TEntity>(
            DbSet<TEntity> source,
            List<TEntity> target,
            string tableName,
            ILoggingService logger,
            CancellationToken cancellationToken)
            where TEntity : class
        {
            try
            {
                target.AddRange(await source.AsNoTracking().ToListAsync(cancellationToken));
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 1)
            {
                logger.Warning($"旧版数据库不包含可迁移表或字段：{tableName}。");
            }
        }
    }
}
