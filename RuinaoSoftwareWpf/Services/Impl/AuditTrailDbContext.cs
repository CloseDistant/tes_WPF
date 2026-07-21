namespace RuinaoSoftwareWpf;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

internal sealed class AuditTrailDbContext : DbContext
{
    private readonly string databasePath;
    private readonly bool encrypted;

    public AuditTrailDbContext(string databasePath, bool encrypted = true)
    {
        this.databasePath = databasePath;
        this.encrypted = encrypted;
    }

    public DbSet<AuditEventEntity> Events => Set<AuditEventEntity>();

    public DbSet<AuditMetadataEntity> Metadata => Set<AuditMetadataEntity>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite(encrypted
            ? EncryptedSqliteDatabase.CreateConnectionString(databasePath)
            : new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Pooling = false,
                DefaultTimeout = 30
            }.ToString());
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AuditEventEntity>();
        entity.ToTable("audit_events");
        entity.HasKey(item => item.SequenceNo);
        entity.Property(item => item.SequenceNo).HasColumnName("sequence_no").ValueGeneratedNever();
        entity.Property(item => item.EventId).HasColumnName("event_id").IsRequired();
        entity.Property(item => item.OccurredAtUtcMs).HasColumnName("occurred_at_utc_ms");
        entity.Property(item => item.ActorUserId).HasColumnName("actor_user_id");
        entity.Property(item => item.ActorLoginName).HasColumnName("actor_login_name").IsRequired().UseCollation("NOCASE");
        entity.Property(item => item.ActorRoleId).HasColumnName("actor_role_id");
        entity.Property(item => item.SessionId).HasColumnName("session_id").IsRequired();
        entity.Property(item => item.EventCategory).HasColumnName("event_category");
        entity.Property(item => item.ActionCode).HasColumnName("action_code").IsRequired();
        entity.Property(item => item.TargetType).HasColumnName("target_type").IsRequired();
        entity.Property(item => item.TargetId).HasColumnName("target_id").IsRequired();
        entity.Property(item => item.Result).HasColumnName("result");
        entity.Property(item => item.FailureCode).HasColumnName("failure_code").IsRequired();
        entity.Property(item => item.Reason).HasColumnName("reason").IsRequired();
        entity.Property(item => item.WorkstationId).HasColumnName("workstation_id").IsRequired();
        entity.Property(item => item.SoftwareVersion).HasColumnName("software_version").IsRequired();

        entity.HasIndex(item => item.EventId).IsUnique();
        entity.HasIndex(item => item.OccurredAtUtcMs);
        entity.HasIndex(item => new { item.EventCategory, item.OccurredAtUtcMs });
        entity.HasIndex(item => new { item.ActorLoginName, item.OccurredAtUtcMs });

        var metadata = modelBuilder.Entity<AuditMetadataEntity>();
        metadata.ToTable("audit_metadata");
        metadata.HasKey(item => item.Key);
        metadata.Property(item => item.Key).HasColumnName("metadata_key");
        metadata.Property(item => item.Value).HasColumnName("metadata_value").IsRequired();
    }
}

internal sealed class AuditEventEntity
{
    public long SequenceNo { get; set; }
    public string EventId { get; set; } = string.Empty;
    public long OccurredAtUtcMs { get; set; }
    public long? ActorUserId { get; set; }
    public string ActorLoginName { get; set; } = string.Empty;
    public int? ActorRoleId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public int EventCategory { get; set; }
    public string ActionCode { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public int Result { get; set; }
    public string FailureCode { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string WorkstationId { get; set; } = string.Empty;
    public string SoftwareVersion { get; set; } = string.Empty;
}

internal sealed class AuditMetadataEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
