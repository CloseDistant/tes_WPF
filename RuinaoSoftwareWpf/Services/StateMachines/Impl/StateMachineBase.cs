namespace RuinaoSoftwareWpf;

/// <summary>
/// 通用状态机基类，负责保存当前状态并发出状态转换事件。
/// </summary>
public abstract class StateMachineBase<TState>
{
    private readonly IAuditLogService auditLog;

    protected StateMachineBase(TState initialState, IAuditLogService auditLog)
    {
        CurrentState = initialState;
        this.auditLog = auditLog;
    }

    public TState CurrentState { get; private set; }

    protected StateTransition<TState> MoveCore(TState nextState, string trigger, string operatorId)
    {
        var transition = new StateTransition<TState>(
            CurrentState,
            nextState,
            trigger,
            DateTimeOffset.Now,
            operatorId);

        CurrentState = nextState;
        auditLog.RecordStateTransition(transition);
        return transition;
    }
}
