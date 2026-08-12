namespace RuinaoSoftwareWpf.ApplicationContracts;

public sealed record EegSampleBlock(
    double[][] ChannelSamples, long StartSampleIndex, int SampleCount, DateTimeOffset ReceivedAt);
