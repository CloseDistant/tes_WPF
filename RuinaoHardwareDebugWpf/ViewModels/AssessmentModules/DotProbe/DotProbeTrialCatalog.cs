namespace RuinaoHardwareDebugWpf;

/// <summary>
/// 点探测固定试次表。
/// 顺序和类型来自业务端提供的 1-48 号配置，正式执行期间不得随机化。
/// </summary>
internal static class DotProbeTrialCatalog
{
    public const string ConfigurationVersion = "dot_probe_v1";

    public static IReadOnlyList<DotProbeTrialDefinition> Trials { get; } =
    [
        Trial(1, "znzr23345h001.png", 3, "znzr23345h007.png", 3, 3, 1),
        Trial(2, "jjrw23345h001.png", 1, "znzr23345h044.png", 3, 3, 1),
        Trial(3, "znzr23345h040.png", 3, "fxzr23345h009.png", 2, 3, 1),
        Trial(4, "jjrw23345h007.png", 1, "znzr23345h025.png", 3, 4, 2),
        Trial(5, "znzr23345h004.png", 3, "znzr23345h002.png", 3, 4, 2),
        Trial(6, "fxzr23345h001.png", 2, "znzr23345h002.png", 3, 3, 1),
        Trial(7, "jjrw23345h011.png", 1, "znzr23345h011.png", 3, 4, 2),
        Trial(8, "fxzr23345h014.png", 2, "znzr23345h044.png", 3, 4, 2),
        Trial(9, "znzr23345h017.png", 3, "znzr23345h020.png", 3, 3, 1),
        Trial(10, "znzr23345h034.png", 3, "fxzr23345h013.png", 2, 3, 1),
        Trial(11, "jjrw23345h015.png", 1, "znzr23345h013.png", 3, 3, 1),
        Trial(12, "znzr23345h037.png", 3, "znzr23345h042.png", 3, 4, 2),
        Trial(13, "znzr23345h012.png", 3, "jjrw23345h014.png", 1, 3, 1),
        Trial(14, "fxzr23345h008.png", 2, "znzr23345h022.png", 3, 3, 1),
        Trial(15, "znzr23345h032.png", 3, "znzr23345h033.png", 3, 4, 2),
        Trial(16, "jjrw23345h017.png", 1, "znzr23345h017.png", 3, 3, 1),
        Trial(17, "znzr23345h030.png", 3, "znzr23345h031.png", 3, 3, 1),
        Trial(18, "znzr23345h025.png", 3, "fxzr23345h011.png", 2, 4, 2),
        Trial(19, "znzr23345h034.png", 3, "znzr23345h044.png", 3, 3, 1),
        Trial(20, "jjrw23345h009.png", 1, "znzr23345h011.png", 3, 4, 2),
        Trial(21, "znzr23345h029.png", 3, "fxzr23345h017.png", 2, 3, 1),
        Trial(22, "znzr23345h029.png", 3, "jjrw23345h012.png", 1, 4, 2),
        Trial(23, "fxzr23345h018.png", 2, "znzr23345h030.png", 3, 4, 2),
        Trial(24, "znzr23345h040.png", 3, "znzr23345h031.png", 3, 3, 1),
        Trial(25, "znzr23345h030.png", 3, "znzr23345h043.png", 3, 4, 2),
        Trial(26, "fxzr23345h012.png", 2, "znzr23345h026.png", 3, 3, 1),
        Trial(27, "jjrw23345h003.png", 1, "znzr23345h022.png", 3, 4, 2),
        Trial(28, "znzr23345h028.png", 3, "fxzr23345h015.png", 2, 4, 2),
        Trial(29, "jjrw23345h005.png", 1, "znzr23345h025.png", 3, 4, 2),
        Trial(30, "znzr23345h032.png", 3, "znzr23345h038.png", 3, 4, 2),
        Trial(31, "znzr23345h032.png", 3, "jjrw23345h018.png", 1, 4, 2),
        Trial(32, "fxzr23345h017.png", 2, "znzr23345h028.png", 3, 4, 2),
        Trial(33, "znzr23345h044.png", 3, "znzr23345h035.png", 3, 3, 1),
        Trial(34, "znzr23345h034.png", 3, "znzr23345h044.png", 3, 3, 1),
        Trial(35, "jjrw23345h019.png", 1, "znzr23345h033.png", 3, 3, 1),
        Trial(36, "fxzr23345h018.png", 2, "znzr23345h007.png", 3, 4, 2),
        Trial(37, "znzr23345h004.png", 3, "znzr23345h017.png", 3, 4, 2),
        Trial(38, "znzr23345h032.png", 3, "fxzr23345h008.png", 2, 4, 2),
        Trial(39, "jjrw23345h005.png", 1, "znzr23345h035.png", 3, 3, 1),
        Trial(40, "znzr23345h034.png", 3, "jjrw23345h004.png", 1, 3, 1),
        Trial(41, "znzr23345h033.png", 3, "znzr23345h029.png", 3, 3, 1),
        Trial(42, "znzr23345h017.png", 3, "fxzr23345h011.png", 2, 3, 1),
        Trial(43, "jjrw23345h017.png", 1, "znzr23345h037.png", 3, 3, 1),
        Trial(44, "znzr23345h013.png", 3, "znzr23345h030.png", 3, 4, 2),
        Trial(45, "fxzr23345h009.png", 2, "znzr23345h002.png", 3, 3, 1),
        Trial(46, "znzr23345h002.png", 3, "znzr23345h022.png", 3, 4, 2),
        Trial(47, "znzr23345h035.png", 3, "fxzr23345h013.png", 2, 4, 2),
        Trial(48, "znzr23345h038.png", 3, "jjrw23345h018.png", 1, 3, 1)
    ];

    private static DotProbeTrialDefinition Trial(
        int index,
        string topImage,
        int topType,
        string bottomImage,
        int bottomType,
        int probePosition,
        int correctResponse)
        => new(
            index,
            topImage,
            topType,
            bottomImage,
            bottomType,
            (DotProbePosition)probePosition,
            (DotProbeResponse)correctResponse);
}
