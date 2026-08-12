namespace RuinaoSoftwareWpf.ApplicationContracts;

public sealed record AssessmentModuleDefinition(
    string Code, string DisplayNameKey, AssessmentModuleKind Kind, bool IsDevelopmentOnly);
