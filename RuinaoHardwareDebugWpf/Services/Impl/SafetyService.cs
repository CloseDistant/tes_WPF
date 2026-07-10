namespace RuinaoHardwareDebugWpf;

/// <summary>
/// 安全监控默认实现。
/// 当前提供 P0 框架和基础阈值，真实硬件数据接入后在这里扩展规则。
/// </summary>
public sealed class SafetyService : ISafetyService
{
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
