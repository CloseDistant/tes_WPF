namespace RuinaoHardwareDebugWpf;

/// <summary>
/// 审计日志默认实现。
/// 当前审计日志只写入应用日志文件，不写入 SQLite；
/// 采集数据库只保存采集业务数据、模块事件和传感器样本。
/// </summary>
public sealed class AuditLogService : IAuditLogService
{
    private readonly ILoggingService logger;

    public AuditLogService(ILoggingService logger)
    {
        this.logger = logger;
    }

    public void RecordStateTransition<TState>(StateTransition<TState> transition)
    {
        logger.Info($"AUDIT STATE {typeof(TState).Name} {transition.From} -> {transition.To} trigger={transition.Trigger} operator={transition.OperatorId}");
    }

    public void RecordUserAction(string action, string operatorId = "system")
    {
        logger.Info($"AUDIT USER action={action} operator={operatorId}");
    }

    public void RecordHardwareCommunication(string direction, string command, string details)
    {
        logger.Hardware($"AUDIT {direction} command={command} details={details}");
    }

    public void RecordSafetyEvent(SafetyEvaluationResult result, string operatorId = "system")
    {
        logger.Warning($"AUDIT SAFETY action={result.Action} reason={result.Reason} operator={operatorId}");
    }
}
