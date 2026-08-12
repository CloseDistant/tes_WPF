namespace RuinaoSoftwareWpf;

internal sealed class AssessmentModuleAttemptEntity
{
    public long Id { get; set; }
    public long RunId { get; set; }
    public string SessionKey { get; set; } = string.Empty;
    public string ModuleCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public int ModuleIndex { get; set; }
    public int AttemptNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ResultJson { get; set; }
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
    public long StartedAtUnixMs { get; set; }
    public long? EndedAtUnixMs { get; set; }
    public long CreatedAtUnixMs { get; set; }
    public long UpdatedAtUnixMs { get; set; }
    public AssessmentRunEntity? Run { get; set; }
}
