namespace RuinaoSoftwareWpf.ApplicationContracts;

public sealed record StimulationProgram(
    string ProgramId, string DisplayName, string StimulationType,
    StimulationDeliveryMode DeliveryMode, IReadOnlyList<StimulationChannelParameters> Channels);
