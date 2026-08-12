namespace RuinaoSoftwareWpf;

public sealed record StimulationChannelTreatmentRecord(
    long Id,
    string ChannelName,
    StimulationTreatmentStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    StimulationEndType? EndType,
    string? EndReasonCode,
    string? EndReasonDetail,
    long? PlannedTotalCount,
    long? CompletedCount);
