namespace RuinaoSoftwareWpf;

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
