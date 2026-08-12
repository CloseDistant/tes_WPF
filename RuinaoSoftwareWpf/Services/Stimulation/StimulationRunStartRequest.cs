namespace RuinaoSoftwareWpf;

public sealed record StimulationRunStartRequest(
    string GroupTitle,
    string StimulationType,
    string? PrescriptionName,
    IReadOnlyList<StimulationChannelStartRequest> Channels);
