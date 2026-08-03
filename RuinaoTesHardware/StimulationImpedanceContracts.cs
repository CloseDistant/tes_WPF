namespace RuinaoTesHardware;

/// <summary>电刺激业务板单个物理通道的阻抗读取结果。</summary>
public sealed record TesStimulationImpedanceChannel(
    int PhysicalChannelNumber,
    ushort RegisterAddress,
    uint RawValue)
{
    /// <summary>下位机按实际阻抗乘以100上传，此处统一换算为Ω。</summary>
    public decimal ImpedanceOhms => RawValue / 100m;
}

/// <summary>一次读取单块电刺激业务板8个物理通道所得的完整快照。</summary>
public sealed record TesStimulationImpedanceSnapshot(
    byte BoardAddress,
    IReadOnlyList<TesStimulationImpedanceChannel> Channels,
    TimeSpan Elapsed,
    DateTimeOffset CapturedAt,
    ushort RequestSequence);
