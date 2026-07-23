namespace RuinaoSoftwareWpf;

/// <summary>
/// 情绪 Oddball 目标图形。数值沿用业务配置：3 为圆形，4 为方形。
/// </summary>
internal enum EmotionOddballShape
{
    Circle = 3,
    Square = 4
}

/// <summary>
/// 情绪 Oddball 作答按钮。数值沿用业务配置：1 为方，2 为圆。
/// </summary>
internal enum EmotionOddballResponse
{
    Square = 1,
    Circle = 2
}

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

