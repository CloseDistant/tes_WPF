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
            await EncryptedSqliteDatabase.EnsureEncryptedAsync(
                databasePath,
                logger,
                CopyPlaintextDatabaseAsync,
                cancellationToken);
            if (File.Exists(databasePath) && new FileInfo(databasePath).Length > 0)
            {
                await UpgradeUnversionedDatabaseIfRequiredAsync(databasePath, cancellationToken);
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

    private async Task CopyPlaintextDatabaseAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var snapshot = await LegacyDatabaseSnapshot.LoadAsync(
            sourcePath,
            logger,
            cancellationToken,
            encrypted: false);
        await using var target = new CaptureDbContext(destinationPath);
        await target.Database.MigrateAsync(cancellationToken);
        snapshot.AddTo(target);
        await target.SaveChangesAsync(cancellationToken);
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

    private async Task UpgradeUnversionedDatabaseIfRequiredAsync(
        string databasePath,
        CancellationToken cancellationToken)
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

        logger.Info("检测到未纳入迁移管理的旧数据库，开始通过EF Core迁移数据。");
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
        logger.Info($"旧数据库迁移完成，原数据库已备份为：{Path.GetFileName(backupPath)}");
    }

    private sealed class LegacyDatabaseSnapshot
    {
        public List<AccountUserEntity> Users { get; } = [];
        public List<AccountAuditLogEntity> AccountAuditLogs { get; } = [];
        public List<PatientEntity> Patients { get; } = [];
        public List<AppStateEntity> AppStates { get; } = [];
        public List<FeatureVisibilityEntity> FeatureVisibilities { get; } = [];
        public List<PrescriptionEntity> Prescriptions { get; } = [];
        public List<StimulationRecordEntity> StimulationRecords { get; } = [];
        public List<AssessmentSessionEntity> AssessmentSessions { get; } = [];
        public List<AssessmentModuleRecordEntity> AssessmentModuleRecords { get; } = [];
        public List<AssessmentEventEntity> AssessmentEvents { get; } = [];
        public List<SensorSampleEntity> SensorSamples { get; } = [];
        public List<SessionTimelineEventEntity> SessionTimelineEvents { get; } = [];
        public List<EegRecordingEntity> EegRecordings { get; } = [];
        public List<EegDataSegmentEntity> EegDataSegments { get; } = [];
        public List<EegMarkerEntity> EegMarkers { get; } = [];

        public static async Task<LegacyDatabaseSnapshot> LoadAsync(
            string databasePath,
            ILoggingService logger,
            CancellationToken cancellationToken,
            bool encrypted = true)
        {
            var result = new LegacyDatabaseSnapshot();
            await using var source = new CaptureDbContext(databasePath, encrypted);
            await LoadOptionalAsync(source.Users, result.Users, "users", logger, cancellationToken);
            await LoadOptionalAsync(source.AccountAuditLogs, result.AccountAuditLogs, "account_audit_logs", logger, cancellationToken);
            await LoadOptionalAsync(source.Patients, result.Patients, "patients", logger, cancellationToken);
            await LoadOptionalAsync(source.AppStates, result.AppStates, "app_state", logger, cancellationToken);
            await LoadOptionalAsync(source.FeatureVisibilities, result.FeatureVisibilities, "feature_visibility", logger, cancellationToken);
            await LoadOptionalAsync(source.Prescriptions, result.Prescriptions, "prescriptions", logger, cancellationToken);
            await LoadOptionalAsync(source.StimulationRecords, result.StimulationRecords, "stimulation_records", logger, cancellationToken);
            await LoadOptionalAsync(source.AssessmentSessions, result.AssessmentSessions, "assessment_sessions", logger, cancellationToken);
            await LoadOptionalAsync(source.AssessmentModuleRecords, result.AssessmentModuleRecords, "assessment_module_records", logger, cancellationToken);
            await LoadOptionalAsync(source.AssessmentEvents, result.AssessmentEvents, "assessment_events", logger, cancellationToken);
            await LoadOptionalAsync(source.SensorSamples, result.SensorSamples, "sensor_samples", logger, cancellationToken);
            await LoadOptionalAsync(source.SessionTimelineEvents, result.SessionTimelineEvents, "session_timeline_events", logger, cancellationToken);
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
            context.Prescriptions.AddRange(Prescriptions);
            context.StimulationRecords.AddRange(StimulationRecords);
            context.AssessmentSessions.AddRange(AssessmentSessions);
            context.AssessmentModuleRecords.AddRange(AssessmentModuleRecords);
            context.AssessmentEvents.AddRange(AssessmentEvents);
            context.SensorSamples.AddRange(SensorSamples);
            context.SessionTimelineEvents.AddRange(SessionTimelineEvents);
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
                logger.Warning($"旧数据库不包含可迁移表或字段：{tableName}。");
            }
        }
    }
}
