namespace RuinaoSoftwareWpf;

public interface IAuditTrailAdministrationService
{
    Task<IReadOnlyList<string>> GetActorLoginNamesAsync(
        CancellationToken cancellationToken = default);

    Task<AuditQueryResult> QueryAsync(
        AuditQuery query,
        CancellationToken cancellationToken = default);

    Task<AuditExportResult> ExportCsvAsync(
        AuditQuery query,
        string filePath,
        CancellationToken cancellationToken = default);
}
