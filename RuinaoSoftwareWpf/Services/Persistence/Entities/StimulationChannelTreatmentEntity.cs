namespace RuinaoSoftwareWpf;

internal sealed class StimulationChannelTreatmentEntity
{
    public long Id { get; set; }
    public long StimulationRunId { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public long StartedAtUnixMs { get; set; }
    public long? EndedAtUnixMs { get; set; }
    public string? EndType { get; set; }
    public string? EndReasonCode { get; set; }
    public string? EndReasonDetail { get; set; }
    public double CurrentMilliamp { get; set; }
    public double PlannedDurationSeconds { get; set; }
    public string Polarity { get; set; } = string.Empty;
    public int ParameterSchemaVersion { get; set; }
    public string ParameterSnapshotJson { get; set; } = string.Empty;
    public long? PlannedTotalCount { get; set; }
    public long? CompletedCount { get; set; }
    public string? DeviceErrorCode { get; set; }
    public long CreatedAtUnixMs { get; set; }
    public long UpdatedAtUnixMs { get; set; }
    public StimulationRunEntity? Run { get; set; }
}
