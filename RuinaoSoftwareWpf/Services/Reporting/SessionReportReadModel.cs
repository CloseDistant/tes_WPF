namespace RuinaoSoftwareWpf;

public sealed record SessionReportReadModel(
    string SessionKey,
    string PatientCode,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    int TimelineEventCount,
    int ModuleRecordCount);
