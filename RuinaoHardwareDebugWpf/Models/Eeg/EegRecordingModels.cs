namespace RuinaoHardwareDebugWpf;

public sealed record EegRecordingInfo(
    long Id,
    CaptureSessionInfo CaptureSession,
    string RecordName,
    string OutputDirectory,
    int SegmentSeconds);

public sealed record EegDataSegmentInfo(
    int SegmentIndex,
    string RelativePath,
    long StartSampleIndex,
    long SampleCount,
    long StartedAtUnixMs,
    long? EndedAtUnixMs,
    long ByteLength,
    string Status);
