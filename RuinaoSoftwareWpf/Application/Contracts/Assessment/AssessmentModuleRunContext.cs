namespace RuinaoSoftwareWpf.ApplicationContracts;

public sealed record AssessmentModuleRunContext(
    long RunId, long AttemptId, int AttemptNumber, string PatientCode, string SessionKey,
    string ModuleCode, string ModuleName, int ModuleIndex, DateTimeOffset StartedAt);
