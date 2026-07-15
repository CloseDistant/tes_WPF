namespace RuinaoHardwareDebugWpf;

internal sealed class AccountUserEntity
{
    public long Id { get; set; }
    public string LoginName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int RoleId { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string PasswordSalt { get; set; } = string.Empty;
    public int FailedLoginAttempts { get; set; }
    public long? LockoutEndUnixMs { get; set; }
    public bool MustChangePassword { get; set; }
    public bool IsActive { get; set; } = true;
    public long CreatedAtUnixMs { get; set; }
    public long UpdatedAtUnixMs { get; set; }
}

internal sealed class AccountAuditLogEntity
{
    public long Id { get; set; }
    public long? OperatorUserId { get; set; }
    public long? TargetUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public string? Message { get; set; }
    public long CreatedAtUnixMs { get; set; }
}
