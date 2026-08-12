namespace RuinaoSoftwareWpf;

internal sealed class AssessmentSessionEntity
{
    public long Id { get; set; }
    public string SessionKey { get; set; } = string.Empty;
    public string PatientCode { get; set; } = string.Empty;
    public long StartedAtUnixMs { get; set; }
    public long? EndedAtUnixMs { get; set; }
    public string Status { get; set; } = string.Empty;
    public string UploadStatus { get; set; } = "local_only";
    public string? UploadBatchId { get; set; }
    public long CreatedAtUnixMs { get; set; }
    public long UpdatedAtUnixMs { get; set; }
}
