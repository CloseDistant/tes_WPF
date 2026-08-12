namespace RuinaoSoftwareWpf;

public sealed record AuditEventInput(
    AuditEventCategory Category,
    string ActionCode,
    AuditActor Actor,
    string TargetType,
    string TargetId,
    AuditEventResult Result,
    string? FailureCode = null,
    string? Reason = null,
    DateTimeOffset? OccurredAtUtc = null);
