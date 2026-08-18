namespace RuinaoSoftwareWpf.ApplicationContracts;

/// <summary>
/// 一次完整数字表型评估的稳定运行上下文。
/// 正式工作台必须由评估入口取得该上下文后才能启动模块。
/// </summary>
public sealed record AssessmentRunContext(
    long RunId,
    string PatientCode,
    int NextModuleIndex,
    int TotalModuleCount,
    DateTimeOffset StartedAt);
