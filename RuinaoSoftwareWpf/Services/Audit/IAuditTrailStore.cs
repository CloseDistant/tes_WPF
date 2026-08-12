namespace RuinaoSoftwareWpf;

internal interface IAuditTrailStore
{
    Task<IReadOnlyList<string>> GetActorLoginNamesAsync(
        CancellationToken cancellationToken = default);

    Task<AuditQueryResult> QueryAsync(
        AuditQuery query,
        CancellationToken cancellationToken = default);
}
