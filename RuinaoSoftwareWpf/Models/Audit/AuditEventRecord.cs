namespace RuinaoSoftwareWpf;

public sealed record AuditEventRecord(
    long SequenceNo,
    string EventId,
    DateTimeOffset OccurredAtUtc,
    long? ActorUserId,
    string ActorLoginName,
    int? ActorRoleId,
    string SessionId,
    AuditEventCategory Category,
    string ActionCode,
    string TargetType,
    string TargetId,
    AuditEventResult Result,
    string? FailureCode,
    string? Reason,
    string WorkstationId,
    string SoftwareVersion);
