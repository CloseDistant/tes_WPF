namespace RuinaoSoftwareWpf;

public sealed class DebugHardwareSimulationService : IDebugHardwareSimulationService
{
    private int isConnected;

    public event EventHandler? ConnectionChanged;

    public bool IsAvailable
    {
        get
        {
#if DEBUG
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
            return new DebugHardwareSimulationResult(false, "模拟联机仅在 DEBUG 构建中可用。");
        }

        if (realHardwareConnected)
        {
            return new DebugHardwareSimulationResult(false, "仪器已真实联机，无需启用模拟联机。");
        }

        if (Interlocked.Exchange(ref isConnected, 1) == 0)
        {
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }

        return new DebugHardwareSimulationResult(true, "DEBUG 模拟联机已启用。");
    }
}
