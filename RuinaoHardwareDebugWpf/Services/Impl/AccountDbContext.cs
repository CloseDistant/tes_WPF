namespace RuinaoHardwareDebugWpf;

using Microsoft.Data.Sqlite;
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
    }
}
