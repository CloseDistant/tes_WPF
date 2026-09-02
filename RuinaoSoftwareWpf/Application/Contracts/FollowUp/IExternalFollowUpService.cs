namespace RuinaoSoftwareWpf;

/// <summary>
/// 网新外部随访接口边界。当前阶段只接入接口 1 的患者分页查询。
/// </summary>
public interface IExternalFollowUpService
{
    Task<ExternalFollowUpPatientPage> SearchPatientsAsync(
        string? phone,
        int pageNumber = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default);
}
