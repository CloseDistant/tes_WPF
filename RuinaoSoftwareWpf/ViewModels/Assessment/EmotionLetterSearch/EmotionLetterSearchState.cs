namespace RuinaoSoftwareWpf;

/// <summary>
/// 情绪字母搜索正式任务内部状态。
/// 每个状态对应唯一画面与计时规则，避免多个布尔值组合出非法状态。
/// </summary>
internal enum EmotionLetterSearchState
{
    Idle,
    Fixation,
    ImageOnly,
    ImageWithLetters,
    PostBlank,
    Resting,
    Completed
}
