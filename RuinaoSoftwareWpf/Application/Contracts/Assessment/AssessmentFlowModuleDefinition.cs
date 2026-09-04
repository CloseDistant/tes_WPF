namespace RuinaoSoftwareWpf.ApplicationContracts;

/// <summary>
/// 当前软件可执行的正式评估模块定义。
/// ModuleTypeId 是永久身份编号，不能因删除、插入或调整顺序而改变或复用。
/// 列表位置只表示新评估采用的执行顺序。
/// </summary>
public sealed record AssessmentFlowModuleDefinition(
    int ModuleTypeId,
    string ModuleCode);
