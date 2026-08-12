namespace RuinaoSoftwareWpf;

internal sealed class AssessmentModuleRecordEntity
{
    public long Id { get; set; }
    public long SessionId { get; set; }
    public long? AssessmentAttemptId { get; set; }
    public string ModuleCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public string RecordType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? CameraName { get; set; }
    public string? OutputDir { get; set; }
    public string? RawVideoPath { get; set; }
    public string? NormalizedVideoPath { get; set; }
    public string? AudioPath { get; set; }
    public string? MergedVideoPath { get; set; }
    public string? FormPayloadJson { get; set; }
    public string? ResultSummary { get; set; }
    public long StartedAtUnixMs { get; set; }
    public long? EndedAtUnixMs { get; set; }
    public long CreatedAtUnixMs { get; set; }
    public long UpdatedAtUnixMs { get; set; }
}
