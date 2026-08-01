namespace RuinaoHardwareEngineer.Features.StimulationImpedance;

public sealed record EngineerStimulationImpedanceChannel(
    int Channel,
    ushort RegisterAddress,
    uint RawValue)
{
    /// <summary>下位机以实际欧姆值乘100后的UInt32上传。</summary>
    public decimal ImpedanceOhms => RawValue / 100m;
}

public sealed record EngineerStimulationImpedanceSnapshot(
    byte BoardAddress,
    IReadOnlyList<EngineerStimulationImpedanceChannel> Channels,
    TimeSpan Elapsed,
    DateTimeOffset ReadTime,
    ushort RequestSequence);
