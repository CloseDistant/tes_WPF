namespace RuinaoHardwareDebugWpf;

/// <summary>
/// 安全监控服务接口。
/// 后续温度、阻抗、电流密度、通信丢失等规则都应集中在这里评估。
/// </summary>
public interface ISafetyService
{
    /// <summary>根据传感器快照评估是否需要报警、停止或急停。</summary>
    Task<SafetyEvaluationResult> EvaluateAsync(SensorSnapshot snapshot, CancellationToken cancellationToken = default);

    /// <summary>刺激启动前的安全前置检查。</summary>
    Task EnsureCanStartStimulationAsync(CancellationToken cancellationToken = default);
}
