namespace RuinaoSoftwareWpf;

public interface IReportReadModelService
{
    Task RefreshSnapshotAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SessionReportReadModel>> GetRecentSessionsAsync(
        int count,
        CancellationToken cancellationToken = default);
}
