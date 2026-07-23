namespace RuinaoSoftwareWpf;

/// <summary>
/// 情绪 Oddball 固定 64 试次表。
/// 顺序、图片类型、目标图形和正确按键来自业务端提供的正式配置，执行期间不得随机化。
/// </summary>
internal static class EmotionOddballTrialCatalog
{
    public const string ConfigurationVersion = "emotion_oddball_v1";

    public static IReadOnlyList<EmotionOddballTrialDefinition> Trials { get; } =
    [
        Trial(1, "znmk98454s036.png", 3, 4, 1),
        Trial(2, "znmk98454s036.png", 3, 4, 1),
        Trial(3, "znmk98454s036.png", 3, 4, 1),
        Trial(4, "znmk98454s004.png", 3, 3, 2),
        Trial(5, "fxmk98454s031.png", 2, 4, 1),
        Trial(6, "fxmk98454s031.png", 2, 4, 1),
        Trial(7, "fxmk98454s031.png", 2, 4, 1),
        Trial(8, "fxmk98454s031.png", 2, 4, 1),
        Trial(9, "znmk98454s077.png", 3, 3, 2),
        Trial(10, "znmk98454s036.png", 3, 4, 1),
        Trial(11, "znmk98454s036.png", 3, 4, 1),
        Trial(12, "znmk98454s036.png", 3, 4, 1),
        Trial(13, "fxmk98454s095.png", 2, 3, 2),
        Trial(14, "fxmk98454s031.png", 2, 4, 1),
        Trial(15, "fxmk98454s031.png", 2, 4, 1),
        Trial(16, "fxmk98454s031.png", 2, 4, 1),
        Trial(17, "znmk98454s051.png", 3, 3, 2),
        Trial(18, "znmk98454s036.png", 3, 4, 1),
        Trial(19, "znmk98454s036.png", 3, 4, 1),
        Trial(20, "znmk98454s036.png", 3, 4, 1),
        Trial(21, "znmk98454s036.png", 3, 4, 1),
        Trial(22, "fxmk98454s093.png", 2, 3, 2),
        Trial(23, "znmk98454s036.png", 3, 4, 1),
        Trial(24, "znmk98454s036.png", 3, 4, 1),
        Trial(25, "fxmk98454s083.png", 2, 3, 2),
        Trial(26, "fxmk98454s031.png", 2, 4, 1),
        Trial(27, "fxmk98454s031.png", 2, 4, 1),
        Trial(28, "fxmk98454s031.png", 2, 4, 1),
        Trial(29, "znmk98454s080.png", 3, 3, 2),
        Trial(30, "fxmk98454s031.png", 2, 4, 1),
        Trial(31, "fxmk98454s031.png", 2, 4, 1),
        Trial(32, "fxmk98454s031.png", 2, 4, 1),
        Trial(33, "fxmk98454s013.png", 2, 3, 2),
        Trial(34, "znmk98454s036.png", 3, 4, 1),
        Trial(35, "znmk98454s036.png", 3, 4, 1),
        Trial(36, "znmk98454s015.png", 3, 3, 2),
        Trial(37, "znmk98454s036.png", 3, 4, 1),
        Trial(38, "znmk98454s036.png", 3, 4, 1),
        Trial(39, "znmk98454s036.png", 3, 4, 1),
        Trial(40, "fxmk98454s022.png", 2, 3, 2),
        Trial(41, "fxmk98454s031.png", 2, 4, 1),
        Trial(42, "fxmk98454s031.png", 2, 4, 1),
        Trial(43, "fxmk98454s031.png", 2, 4, 1),
        Trial(44, "fxmk98454s031.png", 2, 4, 1),
        Trial(45, "fxmk98454s020.png", 2, 3, 2),
        Trial(46, "znmk98454s036.png", 3, 4, 1),
        Trial(47, "znmk98454s036.png", 3, 4, 1),
        Trial(48, "znmk98454s029.png", 3, 3, 2),
        Trial(49, "fxmk98454s031.png", 2, 4, 1),
        Trial(50, "fxmk98454s031.png", 2, 4, 1),
        Trial(51, "fxmk98454s031.png", 2, 4, 1),
        Trial(52, "fxmk98454s031.png", 2, 4, 1),
        Trial(53, "fxmk98454s057.png", 2, 3, 2),
        Trial(54, "znmk98454s036.png", 3, 4, 1),
        Trial(55, "znmk98454s036.png", 3, 4, 1),
        Trial(56, "znmk98454s009.png", 3, 3, 2),
        Trial(57, "znmk98454s036.png", 3, 4, 1),
        Trial(58, "znmk98454s036.png", 3, 4, 1),
        Trial(59, "znmk98454s036.png", 3, 4, 1),
        Trial(60, "fxmk98454s010.png", 2, 3, 2),
        Trial(61, "fxmk98454s031.png", 2, 4, 1),
        Trial(62, "fxmk98454s031.png", 2, 4, 1),
        Trial(63, "fxmk98454s031.png", 2, 4, 1),
        Trial(64, "znmk98454s016.png", 3, 3, 2)
    ];

    private static EmotionOddballTrialDefinition Trial(
        int index,
        string imageFileName,
        int imageType,
        int shape,
        int correctResponse)
        => new(
            index,
            imageFileName,
            imageType,
            (EmotionOddballShape)shape,
            (EmotionOddballResponse)correctResponse);
}

