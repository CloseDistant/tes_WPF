namespace RuinaoSoftwareWpf;

/// <summary>
/// 情绪字母搜索作答。业务配置规定：包含 X 选择 1，包含 N 选择 2。
/// </summary>
internal enum EmotionLetterSearchResponse
{
    ContainsX = 1,
    ContainsN = 2
}

/// <summary>
/// 单个情绪字母搜索试次的固定配置。
/// 图片、字母与分类字段用于还原刺激；界面只展示图片、字母和 1/2 作答按钮。
/// </summary>
internal sealed record EmotionLetterSearchTrialDefinition(
    int TrialIndex,
    string ImageFileName,
    int ImageType,
    string Letters,
    int LetterType,
    int LetterPosition,
    int LoadCategory,
    EmotionLetterSearchResponse CorrectResponse);
