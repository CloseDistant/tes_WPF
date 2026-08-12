namespace RuinaoSoftwareWpf;

internal sealed record StimulationBoardImpedanceReading(
    byte BoardAddress,
    DateTimeOffset CapturedAt,
    IReadOnlyList<StimulationBoardChannelReading> Channels);
