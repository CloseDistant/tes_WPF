namespace RuinaoSoftwareWpf;

internal sealed class AssessmentEventEntity
{
    public long Id { get; set; }
    public long? SessionId { get; set; }
    public long? ModuleRecordId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public long EventTimeUnixMs { get; set; }
    public long? StartedAtUnixMs { get; set; }
    public long? EndedAtUnixMs { get; set; }
    public string? Message { get; set; }
    public string? PayloadJson { get; set; }
    public AssessmentModuleRecordEntity? ModuleRecord { get; set; }
}
