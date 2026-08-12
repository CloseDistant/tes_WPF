namespace RuinaoSoftwareWpf;

internal sealed record CaptureRecordingCompletedEventArgs(
    CaptureSessionInfo Session,
    string Status,
    string Message);
