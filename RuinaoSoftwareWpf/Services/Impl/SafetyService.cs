namespace RuinaoSoftwareWpf;

/// <summary>
/// 安全监控默认实现。
/// 当前提供 P0 框架和基础阈值，真实硬件数据接入后在这里扩展规则。
/// </summary>
public sealed class SafetyService : ISafetyService
{
    // TODO(Hardware interlock): 硬件业务板完成后，在本服务统一接入以下安全联锁：
    // - 设备断开：运行中立即停止刺激；
    // - 阻抗异常：启动前禁止启动，运行中立即停止刺激；
    // - 通信丢失：运行中立即停止刺激。
    // 停止动作必须下发至设备并生成治疗记录与审计日志，不能只改变 UI 状态。
    // 阻抗异常阈值和通信丢失判定时限待硬件协议与产品安全参数确认。
    private const double TemperatureWarningC = 40.0;
    private const double TemperatureCriticalC = 42.0;
    private const double ImpedanceWarningOhm = 10_000.0;
    private const double ImpedanceCriticalOhm = 20_000.0;

    private readonly IAuditLogService auditLog;

    public SafetyService(IAuditLogService auditLog)
    {
        this.auditLog = auditLog;
    }

    public Task<SafetyEvaluationResult> EvaluateAsync(SensorSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = Evaluate(snapshot);
        if (result.Action != SafetyAction.None)
        {
            auditLog.RecordSafetyEvent(result);
        }

        return Task.FromResult(result);
    }

    public Task EnsureCanStartStimulationAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // TODO(Hardware interlock): 取得业务板连接、通信健康和阻抗状态后再允许启动。
        // 任何一个待启动通道阻抗异常时，整次同步启动必须失败且不得下发启动命令。
        return Task.CompletedTask;
    }

    private static SafetyEvaluationResult Evaluate(SensorSnapshot snapshot)
    {
        if (snapshot.TemperaturesC.Any(value => value > TemperatureCriticalC))
        {
            return Result(SafetyAction.EmergencyStop, "温度超过急停阈值");
        }

        if (snapshot.ImpedancesOhm.Any(value => value > ImpedanceCriticalOhm))
        {
            return Result(SafetyAction.Stop, "阻抗超过停止阈值");
        }

        if (snapshot.TemperaturesC.Any(value => value > TemperatureWarningC))
        {
            return Result(SafetyAction.Warning, "温度超过预警阈值");
        }

        if (snapshot.ImpedancesOhm.Any(value => value > ImpedanceWarningOhm))
        {
            return Result(SafetyAction.Warning, "阻抗超过预警阈值");
        }

        return Result(SafetyAction.None, "安全检查通过");
    }

    private static SafetyEvaluationResult Result(SafetyAction action, string reason)
    {
        return new SafetyEvaluationResult(action, reason, DateTimeOffset.Now);
    }
}
