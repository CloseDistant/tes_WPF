namespace RuinaoSoftwareWpf;

/// <summary>正式软件中一个逻辑刺激通道的阻抗快照。</summary>
public sealed record StimulationImpedanceChannelSnapshot(
    int LogicalChannelNumber,
    int? BoardSlotIndex,
    byte? BoardAddress,
    int? PhysicalChannelNumber,
    ushort? RegisterAddress,
    uint? RawValue,
    decimal? ImpedanceOhms,
    DateTimeOffset? LastSuccessfulReadAt)
{
    public bool IsAvailable => ImpedanceOhms.HasValue;
}
