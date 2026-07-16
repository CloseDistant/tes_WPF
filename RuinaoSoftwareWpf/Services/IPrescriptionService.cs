namespace RuinaoSoftwareWpf;

/// <summary>
/// 处方服务接口。
/// 管理与患者、登录账号无关的公用 tDCS 处方。
/// </summary>
public interface IPrescriptionService
{
    /// <summary>获取全部公用处方。</summary>
    Task<IReadOnlyList<PrescriptionDefinition>> GetPrescriptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>创建一个指定刺激模式的草稿处方。</summary>
    Task<PrescriptionDefinition> CreateDraftAsync(CancellationToken cancellationToken = default);

    /// <summary>保存处方。</summary>
    Task SaveAsync(PrescriptionDefinition prescription, CancellationToken cancellationToken = default);

    /// <summary>复制处方，并生成不重复的数字后缀名称。</summary>
    Task<PrescriptionDefinition> CopyAsync(string prescriptionId, CancellationToken cancellationToken = default);

    /// <summary>删除指定处方。</summary>
    Task DeleteAsync(string prescriptionId, CancellationToken cancellationToken = default);
}
