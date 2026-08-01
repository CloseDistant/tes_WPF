namespace RuinaoHardwareEngineer.Features.RawStimulation;

public sealed record UsbTest4RawWaveform(
    uint WaveformType,
    uint DurationUs,
    uint FrequencyHz,
    uint Amplitude,
    uint Offset,
    uint PhaseDegree,
    uint DutyPermilleOrOrder,
    uint LowLevelOrPositiveValue,
    uint HighLevelOrNegativeValue,
    uint RisePermilleOrPositiveDurationUs,
    uint HoldPermilleOrInterphaseIntervalUs,
    uint FallPermilleOrNegativeDurationUs,
    uint CustomIdOrSeedOrPeriodIntervalUs,
    uint SampleCount,
    uint RepeatCount,
    uint Flags);
