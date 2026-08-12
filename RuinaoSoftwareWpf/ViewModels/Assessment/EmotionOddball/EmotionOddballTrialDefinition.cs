namespace RuinaoSoftwareWpf;

/// <summary>
/// 单个情绪 Oddball 试次配置。
/// 图片类型保留给后续算法使用，不在界面展示，也不重复写入每条试次事件。
/// </summary>
internal sealed record EmotionOddballTrialDefinition(
    int TrialIndex,
    string ImageFileName,
    int ImageType,
    EmotionOddballShape Shape,
    EmotionOddballResponse CorrectResponse);

