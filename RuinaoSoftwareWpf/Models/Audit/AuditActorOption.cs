namespace RuinaoSoftwareWpf;

public sealed record AuditActorOption(string? LoginName, string DisplayName)
{
    public override string ToString() => DisplayName;
}
