namespace RuinaoSoftwareWpf;

/// <summary>
/// 面向界面命令的只读设备联机状态，避免刺激页面依赖完整硬件操作接口。
/// </summary>
public interface IHardwareConnectionState
{
    event EventHandler<HardwareConnectionChangedEventArgs>? ConnectionChanged;

    bool IsConnected { get; }
}
