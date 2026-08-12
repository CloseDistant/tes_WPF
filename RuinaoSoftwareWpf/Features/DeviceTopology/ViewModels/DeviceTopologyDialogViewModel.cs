using System.Collections.ObjectModel;
using System.Windows.Input;

namespace RuinaoSoftwareWpf;

public sealed class DeviceTopologyDialogViewModel : ObservableObject
{
    private readonly IHardwareService hardwareService;
    private readonly ILoggingService logger;
    private string summaryText = "尚未取得设备拓扑。";
    private string capturedAtText = "—";
    private string slotBitmapText = "—";
    private string errorText = string.Empty;
    private string refreshFeedbackText = string.Empty;

    public DeviceTopologyDialogViewModel(IHardwareService hardwareService, ILoggingService logger)
    {
        this.hardwareService = hardwareService;
        this.logger = logger;
        RefreshCommand = new AsyncRelayCommand(
            RefreshAsync,
            () => hardwareService.IsConnected,
            HandleRefreshError);
    }

    public ObservableCollection<DeviceTopologySlotViewModel> Slots { get; } = [];

    public ICommand RefreshCommand { get; }

    public string SummaryText
    {
        get => summaryText;
        private set => SetProperty(ref summaryText, value);
    }

    public string CapturedAtText
    {
        get => capturedAtText;
        private set => SetProperty(ref capturedAtText, value);
    }

    public string SlotBitmapText
    {
        get => slotBitmapText;
        private set => SetProperty(ref slotBitmapText, value);
    }

    public string ErrorText
    {
        get => errorText;
        private set
        {
            if (SetProperty(ref errorText, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public string RefreshFeedbackText
    {
        get => refreshFeedbackText;
        private set
        {
            if (SetProperty(ref refreshFeedbackText, value))
            {
                OnPropertyChanged(nameof(HasRefreshFeedback));
            }
        }
    }

    public bool HasRefreshFeedback => !string.IsNullOrWhiteSpace(RefreshFeedbackText);

    public void LoadCurrentSnapshot()
    {
        ErrorText = string.Empty;
        RefreshFeedbackText = string.Empty;
        if (hardwareService.CurrentDeviceTopology is { } snapshot)
        {
            ApplySnapshot(snapshot);
            return;
        }

        Slots.Clear();
        SummaryText = hardwareService.IsConnected
            ? "联机成功，但尚未取得设备拓扑；请点击“重新扫描”。"
            : "仪器未联机，无法读取设备拓扑。";
        CapturedAtText = "—";
        SlotBitmapText = "—";
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        ErrorText = string.Empty;
        RefreshFeedbackText = "正在重新扫描设备拓扑…";
        var snapshot = await hardwareService.RefreshDeviceTopologyAsync(cancellationToken);
        ApplySnapshot(snapshot);
        RefreshFeedbackText = $"刷新成功：已插板 {snapshot.Slots.Count(slot => slot.IsInserted)}，在线 {snapshot.Slots.Count(slot => slot.IsOnline)}。";
    }

    private void ApplySnapshot(DeviceTopologySnapshot snapshot)
    {
        Slots.Clear();
        foreach (var slot in snapshot.Slots.OrderBy(slot => slot.SlotIndex))
        {
            Slots.Add(new DeviceTopologySlotViewModel(slot));
        }

        var insertedCount = snapshot.Slots.Count(slot => slot.IsInserted);
        var onlineCount = snapshot.Slots.Count(slot => slot.IsOnline);
        SummaryText = $"8个槽位 · 已插板 {insertedCount} · 在线 {onlineCount}";
        CapturedAtText = snapshot.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss");
        SlotBitmapText = $"0x{snapshot.SlotBitmap:X8}";
    }

    private void HandleRefreshError(Exception exception)
    {
        logger.Error("设备拓扑刷新失败", exception);
        RefreshFeedbackText = string.Empty;
        ErrorText = $"扫描失败：{exception.Message}";
    }
}
