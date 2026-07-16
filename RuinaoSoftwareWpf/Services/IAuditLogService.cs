namespace RuinaoSoftwareWpf;

/// <summary>
/// 审计日志服务接口。
/// 与普通 Debug 运行日志不同，审计日志用于记录医疗软件关键动作、状态转换和安全事件。
/// </summary>
public interface IAuditLogService
{
    /// <summary>记录状态机转换。</summary>
    void RecordStateTransition<TState>(StateTransition<TState> transition);

    /// <summary>记录用户操作。</summary>
    void RecordUserAction(string action, string operatorId = "system");

    /// <summary>记录硬件通信摘要。</summary>
    void RecordHardwareCommunication(string direction, string command, string details);

    /// <summary>记录安全事件。</summary>
    void RecordSafetyEvent(SafetyEvaluationResult result, string operatorId = "system");
}
