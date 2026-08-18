namespace RuinaoSoftwareWpf.ApplicationContracts;

public sealed record AssessmentModuleStartRequest(
    long RunId, string PatientCode, string SessionKey, string ModuleCode, string ModuleName,
    int ModuleIndex, int TotalModuleCount);
