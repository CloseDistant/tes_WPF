namespace RuinaoSoftwareWpf;

using Microsoft.EntityFrameworkCore;

internal sealed class AccountDbContext : DbContext
{
    private readonly string databasePath;

    public AccountDbContext(string databasePath)
    {
        this.databasePath = databasePath;
    }

    public DbSet<AccountUserEntity> Users => Set<AccountUserEntity>();

    public DbSet<AccountAuditLogEntity> AccountAuditLogs => Set<AccountAuditLogEntity>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite(EncryptedSqliteDatabase.CreateConnectionString(databasePath));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        AccountDbContextModelConfiguration.Configure(modelBuilder);
    }
}
