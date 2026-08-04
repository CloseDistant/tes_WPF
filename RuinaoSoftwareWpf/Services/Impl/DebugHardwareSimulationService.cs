namespace RuinaoSoftwareWpf;

public sealed class DebugHardwareSimulationService : IDebugHardwareSimulationService
{
    private int isConnected;

    public event EventHandler? ConnectionChanged;

    public bool IsAvailable
    {
        get
        {
#if DEBUG || EXHIBITION
            return true;
#else
            return false;
#endif
        }
    }

    public bool IsConnected => IsAvailable && Volatile.Read(ref isConnected) == 1;

    public DebugHardwareSimulationResult Connect(bool realHardwareConnected)
    {
        if (!IsAvailable)
        {
            return new DebugHardwareSimulationResult(
                false,
                "设备测试联机仅在内部测试版本中可用。"
            );
        }

        if (realHardwareConnected)
        {
            return new DebugHardwareSimulationResult(false, "设备已经联机，无需再次启用测试联机。");
        }

        if (Interlocked.Exchange(ref isConnected, 1) == 0)
        {
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }

        return new DebugHardwareSimulationResult(true, "设备联机已启用。");
    }

    public DebugHardwareSimulationResult Disconnect()
    {
        if (Interlocked.Exchange(ref isConnected, 0) == 1)
        {
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }

        return new DebugHardwareSimulationResult(true, "设备连接已断开。");
    }
}
