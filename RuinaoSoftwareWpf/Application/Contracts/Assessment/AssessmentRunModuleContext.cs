namespace RuinaoSoftwareWpf.ApplicationContracts;

/// <summary>
/// 一次评估创建时保存的模块顺序快照。
/// </summary>
public sealed record AssessmentRunModuleContext(
    int ModuleTypeId,
    string ModuleCode,
    int Sequence,
    string Status);
