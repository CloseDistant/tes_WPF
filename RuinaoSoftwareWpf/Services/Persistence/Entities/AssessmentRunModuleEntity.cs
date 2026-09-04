namespace RuinaoSoftwareWpf;

internal sealed class AssessmentRunModuleEntity
{
    public long Id { get; set; }
    public long RunId { get; set; }
    public int ModuleTypeId { get; set; }
    public string ModuleCode { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public string Status { get; set; } = string.Empty;
    public long CreatedAtUnixMs { get; set; }
    public long UpdatedAtUnixMs { get; set; }
    public AssessmentRunEntity? Run { get; set; }
}
