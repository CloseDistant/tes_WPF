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

/// <summary>CH1～CH16在同一时刻可供界面使用的阻抗快照。</summary>
public sealed record StimulationImpedanceSnapshot(
    DateTimeOffset CapturedAt,
    IReadOnlyList<StimulationImpedanceChannelSnapshot> Channels);

public sealed record StimulationImpedanceChangedEventArgs(
    StimulationImpedanceSnapshot? Snapshot);

internal sealed record StimulationBoardChannelReading(
    int PhysicalChannelNumber,
    ushort RegisterAddress,
    uint RawValue,
    decimal ImpedanceOhms);

internal sealed record StimulationBoardImpedanceReading(
    byte BoardAddress,
    DateTimeOffset CapturedAt,
    IReadOnlyList<StimulationBoardChannelReading> Channels);
