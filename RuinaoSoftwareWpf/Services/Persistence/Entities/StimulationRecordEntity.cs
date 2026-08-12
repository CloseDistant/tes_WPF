namespace RuinaoSoftwareWpf;

internal sealed class StimulationRecordEntity
{
    public long Id { get; set; }
    public long? OperatorUserId { get; set; }
    public string? PatientCode { get; set; }
    public string Action { get; set; } = string.Empty;
    public string GroupTitle { get; set; } = string.Empty;
    public string SelectedChannelNames { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? StimulationType { get; set; }
    public string? PrescriptionName { get; set; }
    public string AdverseReactionRecord { get; set; } = string.Empty;
    public string? ParameterSnapshotJson { get; set; }
    public long EventTimeUnixMs { get; set; }
}
