namespace RuinaoSoftwareWpf;

internal sealed class StimulationRunEntity
{
    public long Id { get; set; }
    public string RunId { get; set; } = string.Empty;
    public long? OperatorUserId { get; set; }
    public string? PatientCode { get; set; }
    public string StimulationType { get; set; } = string.Empty;
    public string? PrescriptionName { get; set; }
    public string GroupTitle { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long StartedAtUnixMs { get; set; }
    public long? EndedAtUnixMs { get; set; }
    public long CreatedAtUnixMs { get; set; }
    public long UpdatedAtUnixMs { get; set; }
    public ICollection<StimulationChannelTreatmentEntity> Channels { get; set; } = [];
}
