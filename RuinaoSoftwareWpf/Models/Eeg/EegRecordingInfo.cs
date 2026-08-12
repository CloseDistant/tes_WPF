namespace RuinaoSoftwareWpf;

public sealed record EegRecordingInfo(
    long Id,
    CaptureSessionInfo CaptureSession,
    string RecordName,
    string OutputDirectory,
    int SegmentSeconds);
