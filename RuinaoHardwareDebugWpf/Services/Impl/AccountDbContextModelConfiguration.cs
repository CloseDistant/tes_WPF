namespace RuinaoHardwareDebugWpf;

using Microsoft.EntityFrameworkCore;

internal static class AccountDbContextModelConfiguration
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        ConfigureUser(modelBuilder);
        ConfigureAuditLog(modelBuilder);
    }

    private static void ConfigureUser(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AccountUserEntity>();
        entity.ToTable("users");
        entity.HasKey(item => item.Id);
        entity.HasIndex(item => item.LoginName).IsUnique();
        entity.Property(item => item.LoginName).HasColumnName("login_name").UseCollation("NOCASE");
        entity.Property(item => item.DisplayName).HasColumnName("display_name");
        entity.Property(item => item.RoleId).HasColumnName("role_id");
        entity.Property(item => item.PasswordHash).HasColumnName("password_hash");
        entity.Property(item => item.PasswordSalt).HasColumnName("password_salt");
        entity.Property(item => item.MustChangePassword).HasColumnName("must_change_password");
        entity.Property(item => item.IsActive).HasColumnName("is_active");
        entity.Property(item => item.CreatedAtUnixMs).HasColumnName("created_at_unix_ms");
        entity.Property(item => item.UpdatedAtUnixMs).HasColumnName("updated_at_unix_ms");
    }

    private static void ConfigureAuditLog(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AccountAuditLogEntity>();
        entity.ToTable("account_audit_logs");
        entity.HasKey(item => item.Id);
        entity.HasIndex(item => item.CreatedAtUnixMs);
        entity.HasIndex(item => item.OperatorUserId);
        entity.Property(item => item.OperatorUserId).HasColumnName("operator_user_id");
        entity.Property(item => item.TargetUserId).HasColumnName("target_user_id");
        entity.Property(item => item.Action).HasColumnName("action");
        entity.Property(item => item.Result).HasColumnName("result");
        entity.Property(item => item.Message).HasColumnName("message");
        entity.Property(item => item.CreatedAtUnixMs).HasColumnName("created_at_unix_ms");
    }
}
