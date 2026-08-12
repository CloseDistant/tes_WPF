namespace RuinaoSoftwareWpf;

/// <summary>
/// 点探测正式任务内部状态。
/// 每个状态只对应一种画面和一段固定时长，避免多个布尔状态组合冲突。
/// </summary>
internal enum DotProbeState
{
    Idle,
    PreBlank,
    Fixation,
    Pictures,
    PostBlank,
    Probe,
    Resting,
    Completed
}
