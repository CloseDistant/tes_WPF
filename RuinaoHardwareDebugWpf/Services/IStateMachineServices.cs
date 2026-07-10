namespace RuinaoHardwareDebugWpf;

/// <summary>
/// 设备连接状态机接口。
/// </summary>
public interface IDeviceStateMachine
{
    /// <summary>当前设备连接状态。</summary>
    DeviceConnectionState CurrentState { get; }

    /// <summary>状态变化事件，供审计日志和 UI 状态栏订阅。</summary>
    event EventHandler<StateTransition<DeviceConnectionState>>? StateChanged;

    /// <summary>执行状态转换。</summary>
    void MoveTo(DeviceConnectionState nextState, string trigger, string operatorId = "system");
}

/// <summary>
/// 刺激执行状态机接口。
/// </summary>
public interface IStimulationStateMachine
{
    /// <summary>当前刺激执行状态。</summary>
    StimulationExecutionState CurrentState { get; }

    /// <summary>状态变化事件，供安全监控和审计日志订阅。</summary>
    event EventHandler<StateTransition<StimulationExecutionState>>? StateChanged;

    /// <summary>执行状态转换。</summary>
    void MoveTo(StimulationExecutionState nextState, string trigger, string operatorId = "system");
}

/// <summary>
/// 头模型/FEM 状态机接口。
/// </summary>
public interface IHeadModelStateMachine
{
    /// <summary>当前头模型流程状态。</summary>
    HeadModelState CurrentState { get; }

    /// <summary>状态变化事件。</summary>
    event EventHandler<StateTransition<HeadModelState>>? StateChanged;

    /// <summary>执行状态转换。</summary>
    void MoveTo(HeadModelState nextState, string trigger, string operatorId = "system");
}
