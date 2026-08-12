namespace RuinaoSoftwareWpf;

/// <summary>CH1～CH16在同一时刻可供界面使用的阻抗快照。</summary>
public sealed record StimulationImpedanceSnapshot(
    DateTimeOffset CapturedAt,
    IReadOnlyList<StimulationImpedanceChannelSnapshot> Channels);
