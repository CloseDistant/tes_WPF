namespace RuinaoSoftwareWpf;
using RuinaoSoftwareWpf.ApplicationContracts;
public sealed class VoiceAssessmentModuleViewModel(string code, string key, bool developmentOnly) : AssessmentModuleViewModel(code, key, developmentOnly) { public override AssessmentModuleKind Kind => AssessmentModuleKind.VoiceBaseline; }
