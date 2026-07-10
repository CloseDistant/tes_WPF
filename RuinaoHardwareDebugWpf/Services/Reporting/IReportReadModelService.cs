namespace RuinaoHardwareDebugWpf;

public sealed record SessionReportReadModel(
    string SessionKey,
    string PatientCode,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    int TimelineEventCount,
    int ModuleRecordCount);

public interface IReportReadModelService
{
    Task RefreshSnapshotAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SessionReportReadModel>> GetRecentSessionsAsync(
        int count,
        CancellationToken cancellationToken = default);
}
