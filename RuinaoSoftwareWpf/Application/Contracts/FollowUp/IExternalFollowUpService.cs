namespace RuinaoSoftwareWpf;

/// <summary>
/// 网新外部随访接口边界。当前阶段接入患者分页查询和随访详情查询。
/// </summary>
public interface IExternalFollowUpService
{
    Task<ExternalFollowUpPatientPage> SearchPatientsAsync(
        string? phone,
        int pageNumber = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExternalFollowUpDetail>> GetFollowUpDetailsAsync(
        string phone,
        CancellationToken cancellationToken = default);
}
