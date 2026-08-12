namespace RuinaoSoftwareWpf;
using RuinaoSoftwareWpf.ApplicationContracts;
public sealed class EmotionLetterSearchAssessmentModuleViewModel(string code, string key, bool developmentOnly) : AssessmentModuleViewModel(code, key, developmentOnly) { public override AssessmentModuleKind Kind => AssessmentModuleKind.EmotionLetterSearch; }
