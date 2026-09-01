namespace RuinaoSoftwareWpf;

public sealed record StimulationChannelSnapshot(
    string Name,
    string Anode,
    string Cathode,
    string CurrentMA,
    string RampUpS,
    string RampDownS,
    string DurationS,
    string IntervalS,
    string SingleDurationS,
    string FrequencyHz,
    string Polarity,
    string StimulationMode,
    string PulseWidthMilliseconds = "",
    string PulseRiseWidthMilliseconds = "",
    string PulseIntervalWidthMilliseconds = "",
    string PlannedPulseCount = "");
