namespace RuinaoSoftwareWpf;

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
