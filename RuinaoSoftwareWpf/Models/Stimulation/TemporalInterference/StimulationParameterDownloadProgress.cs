namespace RuinaoSoftwareWpf;

/// <summary>正式刺激开始前的总体参数下发进度。</summary>
public sealed record StimulationParameterDownloadProgress(
    int CompletedCommandCount,
    int TotalCommandCount,
    string CurrentChannel,
    string Stage)
{
    public double Percentage => TotalCommandCount <= 0
        ? 0d
        : Math.Clamp(CompletedCommandCount * 100d / TotalCommandCount, 0d, 100d);
}
