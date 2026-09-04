namespace RuinaoSoftwareWpf.ApplicationContracts;

/// <summary>
/// 一次完整数字表型采集的稳定运行上下文。
/// 正式工作台必须由评估入口取得该上下文后才能启动模块。
/// </summary>
public sealed record AssessmentRunContext(
    long RunId,
    string PatientCode,
    int NextModuleIndex,
    int TotalModuleCount,
    DateTimeOffset StartedAt)
{
    /// <summary>
    /// 下一模块的稳定类型编号；旧记录可为空并在首次读取时生成流程快照。
    /// </summary>
    public int? NextModuleTypeId { get; init; }

    /// <summary>
    /// 本次评估创建时保存的可执行模块顺序。
    /// </summary>
    public IReadOnlyList<AssessmentRunModuleContext> ModuleFlow { get; init; } = [];
}
