namespace RuinaoSoftwareWpf;

/// <summary>
/// 情绪 Stroop 固定 60 试次表。
/// 顺序和分类来自业务端正式配置，执行期间不得随机打乱。
/// </summary>
internal static class EmotionStroopTrialCatalog
{
    public const string ConfigurationVersion = "emotion_stroop_v1";

    public static IReadOnlyList<EmotionStroopTrialDefinition> Trials { get; } =
    [
        Trial(1, "jjmk98454s001.png", 1, "幸福", 11, 1),
        Trial(2, "jjmk98454s006.png", 1, "悲伤", 20, 1),
        Trial(3, "fxmk98454s056.png", 2, "伤心", 20, 2),
        Trial(4, "fxmk98454s080.png", 2, "惊恐", 20, 2),
        Trial(5, "fxmk98454s088.png", 2, "开心", 11, 2),
        Trial(6, "jjmk98454s007.png", 1, "开心", 11, 1),
        Trial(7, "fxmk98454s084.png", 2, "喜悦", 11, 2),
        Trial(8, "fxmk98454s072.png", 2, "幸福", 11, 2),
        Trial(9, "jjmk98454s008.png", 1, "恐惧", 20, 1),
        Trial(10, "fxmk98454s108.png", 2, "高兴", 11, 2),
        Trial(11, "jjmk98454s009.png", 1, "快乐", 11, 1),
        Trial(12, "jjmk98454s013.png", 1, "难过", 20, 1),
        Trial(13, "jjmk98454s011.png", 1, "喜悦", 11, 1),
        Trial(14, "jjmk98454s018.png", 1, "悲伤", 20, 1),
        Trial(15, "fxmk98454s076.png", 2, "沮丧", 20, 2),
        Trial(16, "jjmk98454s029.png", 1, "愤怒", 20, 1),
        Trial(17, "fxmk98454s094.png", 2, "快乐", 11, 2),
        Trial(18, "fxmk98454s082.png", 2, "愤怒", 20, 2),
        Trial(19, "fxmk98454s104.png", 2, "愤怒", 20, 2),
        Trial(20, "jjmk98454s025.png", 1, "满足", 11, 1),
        Trial(21, "jjmk98454s031.png", 1, "沮丧", 20, 1),
        Trial(22, "jjmk98454s055.png", 1, "开心", 11, 1),
        Trial(23, "jjmk98454s032.png", 1, "高兴", 11, 1),
        Trial(24, "fxmk98454s047.png", 2, "哀伤", 20, 2),
        Trial(25, "fxmk98454s061.png", 2, "难过", 20, 2),
        Trial(26, "jjmk98454s052.png", 1, "伤心", 20, 1),
        Trial(27, "jjmk98454s048.png", 1, "难过", 20, 1),
        Trial(28, "fxmk98454s059.png", 2, "满足", 11, 2),
        Trial(29, "fxmk98454s046.png", 2, "喜悦", 11, 2),
        Trial(30, "fxmk98454s039.png", 2, "悲伤", 20, 2),
        Trial(31, "fxmk98454s003.png", 2, "伤心", 20, 2),
        Trial(32, "fxmk98454s064.png", 2, "愉快", 11, 2),
        Trial(33, "jjmk98454s076.png", 1, "惊喜", 11, 1),
        Trial(34, "jjmk98454s059.png", 1, "难过", 20, 1),
        Trial(35, "jjmk98454s049.png", 1, "幸福", 11, 1),
        Trial(36, "fxmk98454s042.png", 2, "幸福", 11, 2),
        Trial(37, "jjmk98454s062.png", 1, "悲伤", 20, 1),
        Trial(38, "jjmk98454s054.png", 1, "愉快", 11, 1),
        Trial(39, "fxmk98454s045.png", 2, "开心", 11, 2),
        Trial(40, "fxmk98454s005.png", 2, "开心", 11, 2),
        Trial(41, "fxmk98454s010.png", 2, "难过", 20, 2),
        Trial(42, "jjmk98454s047.png", 1, "惊恐", 20, 1),
        Trial(43, "jjmk98454s106.png", 1, "愤怒", 20, 1),
        Trial(44, "jjmk98454s037.png", 1, "快乐", 11, 1),
        Trial(45, "fxmk98454s020.png", 2, "喜悦", 11, 2),
        Trial(46, "fxmk98454s038.png", 2, "满足", 11, 2),
        Trial(47, "jjmk98454s081.png", 1, "愉悦", 11, 1),
        Trial(48, "fxmk98454s022.png", 2, "悲伤", 20, 2),
        Trial(49, "fxmk98454s014.png", 2, "沮丧", 20, 2),
        Trial(50, "fxmk98454s026.png", 2, "幸福", 11, 2),
        Trial(51, "jjmk98454s084.png", 1, "喜悦", 11, 1),
        Trial(52, "jjmk98454s091.png", 1, "伤心", 20, 1),
        Trial(53, "fxmk98454s037.png", 2, "惊恐", 20, 2),
        Trial(54, "jjmk98454s072.png", 1, "难过", 20, 1),
        Trial(55, "fxmk98454s040.png", 2, "恐惧", 20, 2),
        Trial(56, "fxmk98454s023.png", 2, "伤心", 20, 2),
        Trial(57, "jjmk98454s028.png", 1, "开心", 11, 1),
        Trial(58, "fxmk98454s029.png", 2, "愉快", 11, 2),
        Trial(59, "jjmk98454s097.png", 1, "快乐", 11, 1),
        Trial(60, "jjmk98454s069.png", 1, "恐惧", 20, 1)
    ];

    private static EmotionStroopTrialDefinition Trial(
        int index,
        string imageFileName,
        int imageType,
        string wordText,
        int wordType,
        int correctResponse)
        => new(
            index,
            imageFileName,
            imageType,
            wordText,
            wordType,
            (EmotionStroopResponse)correctResponse);
}
