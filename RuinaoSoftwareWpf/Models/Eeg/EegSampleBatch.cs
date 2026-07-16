namespace RuinaoSoftwareWpf;

public sealed record EegSampleBatch(
    double[][] ChannelSamples,
    long StartSampleIndex,
    int SampleCount,
    DateTimeOffset ReceivedAt);
