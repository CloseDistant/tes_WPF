namespace RuinaoSoftwareWpf;

internal sealed class PrescriptionEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Indication { get; set; } = string.Empty;
    public string StimulationType { get; set; } = string.Empty;
    public double CurrentMilliamp { get; set; }
    public string DeliveryMode { get; set; } = string.Empty;
    public int TotalDurationMinutes { get; set; }
    public int? IntervalMinutes { get; set; }
    public int? SessionDurationMinutes { get; set; }
    public int? PulseTreatmentDurationSeconds { get; set; }
    public double? PulseTreatmentDurationSecondsExact { get; set; }
    public int? PulseWidthMilliseconds { get; set; }
    public int? PulseRiseWidthMilliseconds { get; set; }
    public int? PulseIntervalWidthMilliseconds { get; set; }
    public double? DirectCurrentTotalDurationSeconds { get; set; }
    public double? DirectCurrentIntervalSeconds { get; set; }
    public double? DirectCurrentSingleDurationSeconds { get; set; }
    public double? DirectCurrentRampUpSeconds { get; set; }
    public double? DirectCurrentRampDownSeconds { get; set; }
    public double? TacsPeakCurrentMilliampere { get; set; }
    public double? TacsRampUpSeconds { get; set; }
    public double? TacsRampDownSeconds { get; set; }
    public int? TacsFrequencyHz { get; set; }
    public double? TacsTotalDurationSeconds { get; set; }
    public int? TacsParameterVersion { get; set; }
    public string Course { get; set; } = string.Empty;
    public int RampUpSeconds { get; set; }
    public int RampDownSeconds { get; set; }
    public string EvidenceGrade { get; set; } = string.Empty;
    public bool IsBuiltin { get; set; }
    public long? CreatedByUserId { get; set; }
    public long? UpdatedByUserId { get; set; }
    public long CreatedAtUnixMs { get; set; }
    public long UpdatedAtUnixMs { get; set; }
}
