namespace RuinaoSoftwareWpf;

public sealed record StimulationChannelStartRequest(
    string ChannelName,
    double CurrentMilliamp,
    double PlannedDurationSeconds,
    string Polarity,
    string ParameterSnapshotJson,
    long? PlannedTotalCount = null);
