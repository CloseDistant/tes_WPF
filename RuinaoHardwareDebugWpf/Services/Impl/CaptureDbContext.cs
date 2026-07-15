namespace RuinaoHardwareDebugWpf;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// 采集工作台 EF Core SQLite 上下文。
/// 只保留连接和 DbSet；具体表映射拆到 CaptureDbContextModelConfiguration。
/// </summary>
internal sealed class CaptureDbContext : DbContext
{
    private readonly string databasePath;

    public CaptureDbContext(string databasePath)
    {
        this.databasePath = databasePath;
    }

    public DbSet<PatientEntity> Patients => Set<PatientEntity>();

    public DbSet<AccountUserEntity> Users => Set<AccountUserEntity>();

    public DbSet<AccountAuditLogEntity> AccountAuditLogs => Set<AccountAuditLogEntity>();

    public DbSet<AppStateEntity> AppStates => Set<AppStateEntity>();

    public DbSet<FeatureVisibilityEntity> FeatureVisibilities => Set<FeatureVisibilityEntity>();

    public DbSet<PrescriptionEntity> Prescriptions => Set<PrescriptionEntity>();

    public DbSet<StimulationRecordEntity> StimulationRecords => Set<StimulationRecordEntity>();

    public DbSet<AssessmentSessionEntity> AssessmentSessions => Set<AssessmentSessionEntity>();

    public DbSet<AssessmentModuleRecordEntity> AssessmentModuleRecords => Set<AssessmentModuleRecordEntity>();

    public DbSet<AssessmentEventEntity> AssessmentEvents => Set<AssessmentEventEntity>();

    // 兼容既有数据库迁移；当前业务不持续保存温度、阻抗等实时采样值。
    public DbSet<SensorSampleEntity> SensorSamples => Set<SensorSampleEntity>();

    public DbSet<SessionTimelineEventEntity> SessionTimelineEvents => Set<SessionTimelineEventEntity>();

    public DbSet<EegRecordingEntity> EegRecordings => Set<EegRecordingEntity>();

    public DbSet<EegDataSegmentEntity> EegDataSegments => Set<EegDataSegmentEntity>();

    public DbSet<EegMarkerEntity> EegMarkers => Set<EegMarkerEntity>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = true,
            DefaultTimeout = 30
        }.ToString();

        optionsBuilder.UseSqlite(connectionString);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        AccountDbContextModelConfiguration.Configure(modelBuilder);
        CaptureDbContextModelConfiguration.Configure(modelBuilder);
    }
}
