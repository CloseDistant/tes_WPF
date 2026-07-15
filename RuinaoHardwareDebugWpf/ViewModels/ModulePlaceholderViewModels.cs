namespace RuinaoHardwareDebugWpf;

/// <summary>
/// 仿真页面 ViewModel（占位）。
/// </summary>
public sealed class SimViewModel : ObservableObject
{
    private readonly ISimulationService simulationService;

    public SimViewModel(ISimulationService simulationService)
    {
        this.simulationService = simulationService;
    }
}

/// <summary>
/// 设备管理页面 ViewModel（占位）。
/// </summary>
public sealed class DeviceViewModel : ObservableObject
{
    private readonly IHardwareService hardwareService;

    public DeviceViewModel(IHardwareService hardwareService)
    {
        this.hardwareService = hardwareService;
    }
}

/// <summary>
/// 未实现页面占位 ViewModel。
/// 显示“该页面框架已接入，功能开发中”之类的提示。
/// </summary>
public sealed class PlaceholderPageViewModel : ObservableObject
{
    private string title = string.Empty;
    private string description = string.Empty;

    /// <summary>占位页标题。</summary>
    public string Title
    {
        get => title;
        private set => SetProperty(ref title, value);
    }

    /// <summary>占位页描述。</summary>
    public string Description
    {
        get => description;
        private set => SetProperty(ref description, value);
    }

    /// <summary>配置标题和描述。</summary>
    public void Configure(string pageTitle, string pageDescription)
    {
        Title = pageTitle;
        Description = pageDescription;
    }
}
