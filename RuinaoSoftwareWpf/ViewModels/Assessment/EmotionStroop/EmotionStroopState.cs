namespace RuinaoSoftwareWpf;

/// <summary>
/// 情绪 Stroop 正式任务状态。
/// 每个状态只对应一种界面和一种可执行操作，避免计时器与按钮状态相互冲突。
/// </summary>
internal enum EmotionStroopState
{
    Idle,
    Fixation,
    Stimulus,
    PostBlank,
    Resting,
    Completed
}
