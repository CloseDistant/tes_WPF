namespace RuinaoSoftwareWpf;

internal sealed class AssessmentRunEntity
{
    public long Id { get; set; }
    public string PatientCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int TotalModuleCount { get; set; }
    public int NextModuleIndex { get; set; }
    public int? NextModuleTypeId { get; set; }
    public long StartedAtUnixMs { get; set; }
    public long? EndedAtUnixMs { get; set; }
    public long CreatedAtUnixMs { get; set; }
    public long UpdatedAtUnixMs { get; set; }
}
