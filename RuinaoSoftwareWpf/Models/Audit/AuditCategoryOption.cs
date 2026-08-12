namespace RuinaoSoftwareWpf;

public sealed record AuditCategoryOption(AuditEventCategory? Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}
