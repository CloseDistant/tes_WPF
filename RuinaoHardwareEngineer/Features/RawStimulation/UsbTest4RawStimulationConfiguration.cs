namespace RuinaoHardwareEngineer.Features.RawStimulation;

public sealed record UsbTest4RawStimulationConfiguration(
    byte BoardAddress,
    uint EnableMask,
    uint ConfigVersion,
    int Channel,
    uint TriggerEnable,
    uint TriggerSource,
    uint TotalTimeMs,
    uint ChannelFlags,
    IReadOnlyList<UsbTest4RawWaveform> Waveforms);
