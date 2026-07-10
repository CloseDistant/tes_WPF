namespace RuinaoHardwareDebugWpf;

/// <summary>
/// 处方服务接口。
/// P1 阶段可在这里扩展内置处方、草稿、保存、应用到控制页面等能力。
/// </summary>
public interface IPrescriptionService
{
    /// <summary>获取处方摘要列表。</summary>
    Task<IReadOnlyList<PrescriptionSummary>> GetPrescriptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>创建一个指定刺激模式的草稿处方。</summary>
    Task<PrescriptionSummary> CreateDraftAsync(string mode, CancellationToken cancellationToken = default);

    /// <summary>保存处方。</summary>
    Task SaveAsync(PrescriptionSummary prescription, CancellationToken cancellationToken = default);
}
