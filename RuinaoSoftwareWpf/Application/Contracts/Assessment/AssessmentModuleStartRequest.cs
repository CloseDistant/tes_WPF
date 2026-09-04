namespace RuinaoSoftwareWpf.ApplicationContracts;

public sealed record AssessmentModuleStartRequest(
    long RunId, string PatientCode, string SessionKey, string ModuleCode, string ModuleName,
    int ModuleTypeId, int ModuleIndex, int TotalModuleCount);
