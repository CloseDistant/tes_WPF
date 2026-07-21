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

/// <summary>
/// 设备连接状态机默认实现。
/// </summary>
public sealed class DeviceStateMachine : StateMachineBase<DeviceConnectionState>, IDeviceStateMachine
{
    public DeviceStateMachine(IAuditLogService auditLog)
        : base(DeviceConnectionState.Disconnected, auditLog)
    {
    }

    public event EventHandler<StateTransition<DeviceConnectionState>>? StateChanged;

    public void MoveTo(DeviceConnectionState nextState, string trigger, string operatorId = "system")
    {
        var transition = MoveCore(nextState, trigger, operatorId);
        StateChanged?.Invoke(this, transition);
    }
}

/// <summary>
/// 刺激执行状态机默认实现。
/// </summary>
public sealed class StimulationStateMachine : StateMachineBase<StimulationExecutionState>, IStimulationStateMachine
{
    public StimulationStateMachine(IAuditLogService auditLog)
        : base(StimulationExecutionState.Idle, auditLog)
    {
    }

    public event EventHandler<StateTransition<StimulationExecutionState>>? StateChanged;

    public void MoveTo(StimulationExecutionState nextState, string trigger, string operatorId = "system")
    {
        var transition = MoveCore(nextState, trigger, operatorId);
        StateChanged?.Invoke(this, transition);
    }
}

/// <summary>
/// 头模型/FEM 状态机默认实现。
/// </summary>
public sealed class HeadModelStateMachine : StateMachineBase<HeadModelState>, IHeadModelStateMachine
{
    public HeadModelStateMachine(IAuditLogService auditLog)
        : base(HeadModelState.NoModel, auditLog)
    {
    }

    public event EventHandler<StateTransition<HeadModelState>>? StateChanged;

    public void MoveTo(HeadModelState nextState, string trigger, string operatorId = "system")
    {
        var transition = MoveCore(nextState, trigger, operatorId);
        StateChanged?.Invoke(this, transition);
    }
}
