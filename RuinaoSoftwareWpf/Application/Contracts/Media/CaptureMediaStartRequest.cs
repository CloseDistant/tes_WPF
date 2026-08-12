namespace RuinaoSoftwareWpf.ApplicationContracts;

public sealed record CaptureMediaStartRequest(
    long? AssessmentAttemptId, string SessionKey, string ModuleCode, string ModuleName, string CameraId);
