namespace RuinaoSoftwareWpf;

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
