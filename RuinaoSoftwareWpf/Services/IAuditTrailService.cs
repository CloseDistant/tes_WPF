namespace RuinaoSoftwareWpf;

public interface IAuditTrailService
{
    event EventHandler<AuditTrailWriteFailedEventArgs>? WriteFailed;

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task AppendAsync(
        AuditEventInput auditEvent,
        CancellationToken cancellationToken = default);

    Task<bool> TryAppendAsync(
        AuditEventInput auditEvent,
        CancellationToken cancellationToken = default);
}

public sealed record AuditTrailWriteFailedEventArgs(string UserMessage);

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

internal interface IAuditTrailStore
{
    Task<IReadOnlyList<string>> GetActorLoginNamesAsync(
        CancellationToken cancellationToken = default);

    Task<AuditQueryResult> QueryAsync(
        AuditQuery query,
        CancellationToken cancellationToken = default);

}
