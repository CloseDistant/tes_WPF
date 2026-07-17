namespace RuinaoSoftwareWpf;

/// <summary>
/// 底部状态栏 ViewModel。
/// 管理设备连接状态、底部状态文字，以及随语言切换变化的在线/离线文本。
/// </summary>
public sealed class ShellStateViewModel : ObservableObject
{
    private readonly LocalizationViewModel localization;
    private bool isDeviceConnected;
    private string footerStatus = "设备：协议库就绪 | 模式：TI | 刺激：空闲";

    public ShellStateViewModel(LocalizationViewModel localization)
    {
        this.localization = localization;

        // 语言切换时，如果在线/离线文字变化，也通知界面刷新。
        this.localization.PropertyChanged += (_, args) =>
        {
            if (string.IsNullOrEmpty(args.PropertyName)
                || args.PropertyName == nameof(LocalizationViewModel.DeviceOnlineText)
                || args.PropertyName == nameof(LocalizationViewModel.DeviceOfflineText))
            {
                OnPropertyChanged(nameof(DeviceStatusText));
            }
        };
    }

    /// <summary>设备是否已连接。变化时会联动刷新 DeviceStatusText。</summary>
    public bool IsDeviceConnected
    {
        get => isDeviceConnected;
        set
        {
            if (SetProperty(ref isDeviceConnected, value))
            {
                OnPropertyChanged(nameof(DeviceStatusText));
            }
        }
    }

    /// <summary>设备状态文字，例如“已联机”或“未联机”，取自本地化服务。</summary>
    public string DeviceStatusText => IsDeviceConnected
        ? localization.DeviceOnlineText
        : localization.DeviceOfflineText;

    /// <summary>底部状态栏文字。</summary>
    public string FooterStatus
    {
        get => footerStatus;
        set => SetProperty(ref footerStatus, value);
    }
}
