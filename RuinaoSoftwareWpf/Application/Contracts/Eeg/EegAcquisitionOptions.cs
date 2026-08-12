namespace RuinaoSoftwareWpf.ApplicationContracts;

public sealed record EegAcquisitionOptions(
    int ChannelCount, int SampleRateHz, IReadOnlyList<string> ChannelNames,
    double? HighPassHz = null, double? LowPassHz = null, double? NotchHz = null,
    int HardwareGain = 1);
