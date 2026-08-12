namespace RuinaoSoftwareWpf;

public sealed record AuditQuery(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    AuditEventCategory? Category,
    string? ActorLoginName,
    int PageNumber = 1,
    int PageSize = 50);
