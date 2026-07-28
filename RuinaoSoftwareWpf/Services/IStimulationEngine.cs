namespace RuinaoSoftwareWpf;

/// <summary>
/// 刺激控制引擎接口。
/// 页面不应直接调用底层硬件启动刺激，而应通过该引擎完成状态机、安全检查和审计记录。
/// </summary>
public interface IStimulationEngine
{
    /// <summary>当前刺激执行状态。</summary>
    StimulationExecutionState CurrentState { get; }

    /// <summary>启动 TI 刺激组。</summary>
    Task<HardwareOperationResult> StartTiGroupAsync(
        TiGroup group,
        string selectedChannelNames,
        string prescriptionName,
        CancellationToken cancellationToken = default);

    /// <summary>启动 tDCS 通道组。</summary>
    Task<HardwareOperationResult> StartDirectCurrentGroupAsync(
        TiGroup group,
        string selectedChannelNames,
        string prescriptionName,
        CancellationToken cancellationToken = default);

    /// <summary>启动一个或两个 tPCS 临时业务板内部通道。</summary>
    Task<HardwareOperationResult> StartPulseCurrentAsync(
        IReadOnlyList<PulseCurrentExecutionChannel> channels,
        string selectedChannelNames,
        string prescriptionName,
        CancellationToken cancellationToken = default);

    /// <summary>暂停 TI 刺激组。</summary>
    Task<HardwareOperationResult> PauseTiGroupAsync(TiGroup group, string selectedChannelNames, CancellationToken cancellationToken = default);

    /// <summary>急停 TI 刺激组。</summary>
    Task<HardwareOperationResult> EmergencyStopTiGroupAsync(TiGroup group, string reason, CancellationToken cancellationToken = default);

    /// <summary>急停 tDCS 通道组。</summary>
    Task<HardwareOperationResult> EmergencyStopDirectCurrentGroupAsync(TiGroup group, string reason, CancellationToken cancellationToken = default);

    /// <summary>急停临时业务板上的全部 tPCS 输出。</summary>
    Task<HardwareOperationResult> EmergencyStopPulseCurrentAsync(
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>通道倒计时自然结束并生成完成记录。</summary>
    Task<HardwareOperationResult> CompleteGroupAsync(
        TiGroup group,
        string selectedChannelNames,
        string stimulationType,
        CancellationToken cancellationToken = default);

    /// <summary>停止自然完成的 tPCS 通道并生成完成记录。</summary>
    Task<HardwareOperationResult> CompletePulseCurrentAsync(
        IReadOnlyList<int> logicalChannelNumbers,
        string selectedChannelNames,
        CancellationToken cancellationToken = default);
}
