namespace RuinaoSoftwareWpf.ApplicationContracts;

public sealed record CaptureMediaSession(
    long SessionId, long? AssessmentAttemptId, string SessionKey, string ModuleCode,
    string ModuleName, string OutputDirectory, DateTimeOffset StartedAt);
