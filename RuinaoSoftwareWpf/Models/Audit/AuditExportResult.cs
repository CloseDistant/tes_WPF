namespace RuinaoSoftwareWpf;

public sealed record AuditExportResult(
    string FilePath,
    long ExportedCount,
    string Sha256,
    DateTimeOffset ExportedAtUtc);
