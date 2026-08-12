namespace RuinaoSoftwareWpf;

/// <summary>
/// 情绪字母搜索固定 48 试次表。
/// 顺序和分类来自业务端正式配置，执行期间不得随机打乱；仅字母出现延迟按规则随机。
/// </summary>
internal static class EmotionLetterSearchTrialCatalog
{
    public const string ConfigurationVersion = "emotion_letter_search_v1";

    public static IReadOnlyList<EmotionLetterSearchTrialDefinition> Trials { get; } =
    [
        Trial(1, "znzr5454s225.png", 3, "VMKVNW", 4, 5, 5, 2),
        Trial(2, "jjrw5454s159.png", 1, "HXVMKV", 3, 2, 5, 1),
        Trial(3, "fxzr5454s076.png", 2, "OOOOXO", 3, 5, 6, 1),
        Trial(4, "jjrw5454s175.png", 1, "ONOOOO", 4, 2, 6, 2),
        Trial(5, "znrw5454s070.png", 3, "OONOOO", 4, 3, 6, 2),
        Trial(6, "fxrw5454s069.png", 2, "OXOOOO", 3, 2, 6, 1),
        Trial(7, "jjrw5454s184.png", 1, "VMKXVW", 3, 4, 5, 1),
        Trial(8, "fxdw5454s030.png", 2, "VMKVNW", 4, 5, 5, 2),
        Trial(9, "znrw5454s084.png", 3, "KHXMWZ", 3, 3, 5, 1),
        Trial(10, "jjrw5454s178.png", 1, "OOXOOO", 3, 3, 6, 1),
        Trial(11, "znrw5454s073.png", 3, "OOOXOO", 3, 4, 6, 1),
        Trial(12, "fxrw5454s085.png", 2, "KHNMWZ", 4, 3, 5, 2),
        Trial(13, "fxrw5454s074.png", 2, "ONOOOO", 4, 2, 6, 2),
        Trial(14, "jjrw5454s174.png", 1, "KHXMWZ", 3, 3, 5, 1),
        Trial(15, "znrw5454s071.png", 3, "MNZKWV", 4, 2, 5, 2),
        Trial(16, "jjrw5454s183.png", 1, "OOONOO", 4, 4, 6, 2),
        Trial(17, "fxrw5454s097.png", 2, "KHXMWZ", 3, 3, 5, 1),
        Trial(18, "znrw5454s075.png", 3, "OOONOO", 4, 4, 6, 2),
        Trial(19, "jjrw5454s160.png", 1, "OONOOO", 4, 3, 6, 2),
        Trial(20, "znzr5454s183.png", 3, "OOOOXO", 3, 5, 6, 1),
        Trial(21, "fxrw5454s102.png", 2, "HXWMKV", 3, 2, 5, 1),
        Trial(22, "jjrw5454s163.png", 1, "KHNMWZ", 4, 3, 5, 2),
        Trial(23, "znzr5454s204.png", 3, "WHKMXZ", 3, 5, 5, 1),
        Trial(24, "fxrw5454s100.png", 2, "HWMNKV", 4, 4, 5, 2),
        Trial(25, "znzr5454s184.png", 3, "OOOONO", 4, 5, 6, 2),
        Trial(26, "jjrw5454s201.png", 1, "OOOXOO", 3, 4, 6, 1),
        Trial(27, "fxrw5454s071.png", 2, "OOXOOO", 3, 3, 6, 1),
        Trial(28, "jjrw5454s169.png", 1, "OOOXOO", 3, 4, 6, 1),
        Trial(29, "znzr5454s207.png", 3, "WHNKMZ", 4, 3, 5, 2),
        Trial(30, "fxrw5454s077.png", 2, "OONOOO", 4, 3, 6, 2),
        Trial(31, "znzr5454s188.png", 3, "OOXOOO", 3, 3, 6, 1),
        Trial(32, "jjrw5454s161.png", 1, "MNZKWV", 4, 2, 5, 2),
        Trial(33, "fxzr5454s082.png", 2, "ZVMNWK", 4, 4, 5, 2),
        Trial(34, "fxrw5454s070.png", 2, "OOONOO", 4, 4, 6, 2),
        Trial(35, "znzr5454s219.png", 3, "ZVMNWK", 4, 4, 5, 2),
        Trial(36, "jjrw5454s171.png", 1, "OOOONO", 4, 5, 6, 2),
        Trial(37, "znzr5454s193.png", 3, "OOOXOO", 3, 4, 6, 1),
        Trial(38, "fxrw5454s086.png", 2, "VMKXVW", 3, 4, 5, 1),
        Trial(39, "jjrw5454s164.png", 1, "WHNKMZ", 4, 3, 5, 2),
        Trial(40, "fxdw5454s028.png", 2, "OOOOXO", 3, 5, 6, 1),
        Trial(41, "znzr5454s210.png", 3, "ZVXMWK", 3, 3, 5, 1),
        Trial(42, "jjrw5454s191.png", 1, "WHKMXZ", 3, 5, 5, 1),
        Trial(43, "jjrw5454s176.png", 1, "OOXOOO", 3, 3, 6, 1),
        Trial(44, "fxzr5454s084.png", 2, "ZVXMWK", 3, 3, 5, 1),
        Trial(45, "znzr5454s221.png", 3, "VMKXVW", 3, 4, 5, 1),
        Trial(46, "znzr5454s195.png", 3, "OOONOO", 4, 4, 6, 2),
        Trial(47, "fxzr5454s083.png", 2, "OOONOO", 4, 4, 6, 2),
        Trial(48, "jjrw5454s177.png", 1, "ZVMNWK", 4, 4, 5, 2)
    ];

    private static EmotionLetterSearchTrialDefinition Trial(
        int index,
        string imageFileName,
        int imageType,
        string letters,
        int letterType,
        int letterPosition,
        int loadCategory,
        int correctResponse)
        => new(
            index,
            imageFileName,
            imageType,
            letters,
            letterType,
            letterPosition,
            loadCategory,
            (EmotionLetterSearchResponse)correctResponse);
}
