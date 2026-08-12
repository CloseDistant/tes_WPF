namespace RuinaoSoftwareWpf.ApplicationContracts;

public sealed record AssessmentProgressSnapshot(
    long? RunId, string PatientCode, AssessmentRunStatus Status,
    int NextModuleIndex, IReadOnlyList<string> CompletedModuleCodes);
