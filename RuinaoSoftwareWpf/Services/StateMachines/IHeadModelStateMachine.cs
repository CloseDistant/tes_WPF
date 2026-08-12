namespace RuinaoSoftwareWpf;

/// <summary>
/// 头模型/FEM 状态机接口。
/// </summary>
public interface IHeadModelStateMachine
{
    HeadModelState CurrentState { get; }

    event EventHandler<StateTransition<HeadModelState>>? StateChanged;

    void MoveTo(HeadModelState nextState, string trigger, string operatorId = "system");
}
