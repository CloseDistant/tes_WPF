namespace RuinaoSoftwareWpf;

public sealed record StimulationChannelsEndRequest(
    string StimulationType,
    IReadOnlyList<StimulationChannelEndItem> Channels,
    StimulationEndType EndType,
    string EndReasonCode,
    string? EndReasonDetail = null);
