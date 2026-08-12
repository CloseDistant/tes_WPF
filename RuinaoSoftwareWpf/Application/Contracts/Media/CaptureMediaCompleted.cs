namespace RuinaoSoftwareWpf.ApplicationContracts;

public sealed record CaptureMediaCompleted(
    CaptureMediaSession Session, CaptureMediaCompletionStatus Status,
    string? ErrorCode, string? Message);
