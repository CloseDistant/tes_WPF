namespace RuinaoSoftwareWpf;

/// <summary>
/// 点探测试次中星号所在的垂直位置。
/// 数值沿用既有业务配置：3 表示上方，4 表示下方。
/// </summary>
internal enum DotProbePosition
{
    Top = 3,
    Bottom = 4
}

/// <summary>
/// 点探测中央作答按钮。
/// 数值沿用既有业务配置：1 表示上方，2 表示下方。
/// </summary>
internal enum DotProbeResponse
{
    Up = 1,
    Down = 2
}

/// <summary>
/// 单个点探测试次配置。
/// 图片类型仅保留给后续算法使用，不在界面展示，也不重复写入每条试次事件。
/// </summary>
internal sealed record DotProbeTrialDefinition(
    int TrialIndex,
    string TopImageFileName,
    int TopImageType,
    string BottomImageFileName,
    int BottomImageType,
    DotProbePosition ProbePosition,
    DotProbeResponse CorrectResponse);
