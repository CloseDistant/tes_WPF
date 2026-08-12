namespace RuinaoSoftwareWpf;

public sealed record ChannelParameterSnapshot(
    int SchemaVersion,
    string ChannelName,
    string Anode,
    string Cathode,
    double CurrentMilliamp,
    double RampUpSeconds,
    double RampDownSeconds,
    double PlannedDurationSeconds,
    double IntervalSeconds,
    double SingleDurationSeconds,
    double? CarrierFrequencyHz,
    string Polarity,
    string DeliveryMode,
    PrescriptionDefinition ReusableParameters,
    int? PulseWidthMilliseconds = null,
    int? PulseRiseWidthMilliseconds = null,
    int? PulseIntervalWidthMilliseconds = null,
    long? PlannedTotalCount = null);
