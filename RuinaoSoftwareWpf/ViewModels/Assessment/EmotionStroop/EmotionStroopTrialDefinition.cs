namespace RuinaoSoftwareWpf;

/// <summary>
/// 单个情绪 Stroop 试次的固定配置。
/// 图片、文字及类型只用于按配置版本还原刺激，数据库不重复保存这些静态内容。
/// </summary>
internal sealed record EmotionStroopTrialDefinition(
    int TrialIndex,
    string ImageFileName,
    int ImageType,
    string WordText,
    int WordType,
    EmotionStroopResponse CorrectResponse);
