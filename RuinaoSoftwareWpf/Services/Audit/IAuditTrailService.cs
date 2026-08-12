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
