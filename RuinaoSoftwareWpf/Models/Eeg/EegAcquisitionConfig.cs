namespace RuinaoSoftwareWpf;

public sealed record EegAcquisitionConfig
{
    public int ChannelCount { get; init; } = 64;

    public int SampleRateHz { get; init; } = 1000;

    public int PageSeconds { get; init; } = 30;

    public double AmplitudeScaleUvPerMm { get; init; } = 50.0;

    public double? HighPassHz { get; init; } = 0.5;

    public double? LowPassHz { get; init; } = 80.0;

    public double? NotchHz { get; init; } = 50.0;

    public int HardwareGain { get; init; } = 10;

    public string ReferenceElectrode { get; init; } = "双侧乳突";

    public IReadOnlyList<string> ChannelNames { get; init; } = EegDefaultChannels.Names;

    public int PageSampleCount => SampleRateHz * PageSeconds;
}
