namespace RuinaoSoftwareWpf;

public sealed record EegDataSegmentInfo(
    int SegmentIndex,
    string RelativePath,
    long StartSampleIndex,
    long SampleCount,
    long StartedAtUnixMs,
    long? EndedAtUnixMs,
    long ByteLength,
    string Status);
