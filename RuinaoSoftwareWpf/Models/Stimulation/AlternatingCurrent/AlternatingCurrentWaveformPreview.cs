namespace RuinaoSoftwareWpf;

/// <summary>正式软件用于绘图的单段交流电阶梯包络快照。</summary>
public sealed record AlternatingCurrentWaveformSegment(
    double StartSeconds,
    double DurationSeconds,
    double PeakCurrentMilliampere);

/// <summary>
/// 从共享硬件DLL计划映射得到的只读绘图快照。
/// 不包含寄存器和协议字段，也不表示硬件实测输出。
/// </summary>
public sealed record AlternatingCurrentWaveformPreview(
    double PeakCurrentMilliampere,
    uint FrequencyHz,
    double TotalDurationSeconds,
    IReadOnlyList<AlternatingCurrentWaveformSegment> Segments);
