namespace RuinaoSoftwareWpf;

/// <summary>设备管理页面 ViewModel（占位）。</summary>
public sealed class DeviceViewModel : ObservableObject
{
    private readonly IHardwareService hardwareService;

    public DeviceViewModel(IHardwareService hardwareService)
    {
        this.hardwareService = hardwareService;
    }
}
