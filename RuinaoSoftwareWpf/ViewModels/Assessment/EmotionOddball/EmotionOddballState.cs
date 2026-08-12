namespace RuinaoSoftwareWpf;

/// <summary>
/// 情绪 Oddball 正式任务内部状态。
/// 每个状态只对应一种画面和固定时长，避免多个布尔状态组合产生冲突。
/// </summary>
internal enum EmotionOddballState
{
    Idle,
    Fixation,
    ImageOnly,
    ImageWithShape,
    PostBlank,
    Completed
}

