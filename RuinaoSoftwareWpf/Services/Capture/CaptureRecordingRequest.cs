namespace RuinaoSoftwareWpf;

internal sealed record CaptureRecordingRequest(
    long? AssessmentAttemptId,
    string SessionKey,
    string ModuleCode,
    string ModuleName,
    string CameraName,
    int? SegmentIndex = null);
