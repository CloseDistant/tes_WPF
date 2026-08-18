namespace RuinaoSoftwareWpf.ApplicationContracts;

/// <summary>
/// 明确区分正式患者评估与 Debug 模块直达，禁止再根据模块序号推测执行模式。
/// </summary>
public enum AssessmentExecutionMode
{
    Formal,
    DevelopmentDirect
}
