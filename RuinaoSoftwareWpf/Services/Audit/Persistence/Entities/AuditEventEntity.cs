namespace RuinaoSoftwareWpf;

internal sealed class AuditEventEntity
{
    public long SequenceNo { get; set; }
    public string EventId { get; set; } = string.Empty;
    public long OccurredAtUtcMs { get; set; }
    public long? ActorUserId { get; set; }
    public string ActorLoginName { get; set; } = string.Empty;
    public int? ActorRoleId { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public int EventCategory { get; set; }
    public string ActionCode { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public int Result { get; set; }
    public string FailureCode { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string WorkstationId { get; set; } = string.Empty;
    public string SoftwareVersion { get; set; } = string.Empty;
}
