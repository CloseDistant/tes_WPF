namespace RuinaoSoftwareWpf;

/// <summary>
/// 安全监控服务接口。
/// 后续温度、阻抗、电流密度、通信丢失等规则都应集中在这里评估。
///
/// 硬件业务板接入后的强制联锁规则：
/// 1. 设备断开：刺激运行中立即停止刺激；
/// 2. 阻抗异常：启动前禁止启动，刺激运行中立即停止刺激；
/// 3. 通信丢失：刺激运行中立即停止刺激。
/// “立即停止”必须同时触发设备停止、通道状态收敛、治疗记录和审计日志，不能只更新界面。
/// </summary>
public interface ISafetyService
{
    /// <summary>根据传感器快照评估是否需要报警、停止或急停。</summary>
    Task<SafetyEvaluationResult> EvaluateAsync(SensorSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>刺激启动前的安全前置检查。</summary>
    Task EnsureCanStartStimulationAsync(CancellationToken cancellationToken = default);
}
