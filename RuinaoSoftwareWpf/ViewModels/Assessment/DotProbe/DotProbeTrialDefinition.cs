namespace RuinaoSoftwareWpf;

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
