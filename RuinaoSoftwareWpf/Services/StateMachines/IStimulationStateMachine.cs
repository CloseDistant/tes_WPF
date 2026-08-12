namespace RuinaoSoftwareWpf;

/// <summary>
/// 刺激执行状态机接口。
/// </summary>
public interface IStimulationStateMachine
{
    StimulationExecutionState CurrentState { get; }

    event EventHandler<StateTransition<StimulationExecutionState>>? StateChanged;

    void MoveTo(StimulationExecutionState nextState, string trigger, string operatorId = "system");
}
