namespace RuinaoSoftwareWpf;

public sealed record AuditQueryResult(
    IReadOnlyList<AuditEventRecord> Items,
    long TotalCount,
    int PageNumber,
    int PageSize);
