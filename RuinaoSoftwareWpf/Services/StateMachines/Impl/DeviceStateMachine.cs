namespace RuinaoSoftwareWpf;

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
