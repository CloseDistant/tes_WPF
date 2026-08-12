namespace RuinaoSoftwareWpf;
using RuinaoSoftwareWpf.ApplicationContracts;
public sealed class GenericAssessmentModuleViewModel(string code, string key, bool developmentOnly, AssessmentModuleKind kind) : AssessmentModuleViewModel(code, key, developmentOnly) { public override AssessmentModuleKind Kind { get; } = kind; }
