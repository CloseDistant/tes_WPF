namespace RuinaoSoftwareWpf;

using System.IO;

internal sealed record AuditTrailStorageOptions(
    string DatabasePath,
    bool ApplyDirectoryAcl)
{
    public static AuditTrailStorageOptions CreateDefault()
    {
        var securityDirectory = Path.Combine(
            Path.GetDirectoryName(AppDatabasePathProvider.MainDatabasePath)!,
            "security");
        return new AuditTrailStorageOptions(
            Path.Combine(securityDirectory, "security_audit.db"),
            ApplyDirectoryAcl: true);
    }
}
