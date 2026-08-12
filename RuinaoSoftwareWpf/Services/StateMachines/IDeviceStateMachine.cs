namespace RuinaoSoftwareWpf;

/// <summary>
/// 设备连接状态机接口。
/// </summary>
public interface IDeviceStateMachine
{
    DeviceConnectionState CurrentState { get; }

    event EventHandler<StateTransition<DeviceConnectionState>>? StateChanged;

    void MoveTo(DeviceConnectionState nextState, string trigger, string operatorId = "system");
}
