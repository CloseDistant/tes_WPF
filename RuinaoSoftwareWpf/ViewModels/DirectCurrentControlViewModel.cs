using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;

namespace RuinaoSoftwareWpf;

/// <summary>经颅直流电刺激页面状态和通道级控制命令。</summary>
public sealed class DirectCurrentControlViewModel : ObservableObject
{
    private readonly IStimulationEngine stimulationEngine;
    private readonly ILoggingService logger;
    private readonly IToastService toastService;
    private readonly StimulationChannelCountdown countdown = new();
    private string appliedPrescriptionName = "手动设置";

    public DirectCurrentControlViewModel(
        IStimulationEngine stimulationEngine,
        ILoggingService logger,
        LocalizationViewModel localization,
        IToastService toastService)
    {
        this.stimulationEngine = stimulationEngine;
        this.logger = logger;
        this.toastService = toastService;
        countdown.Completed += channel => _ = CompleteChannelAsync(channel);
        Localization = localization;

        var accent = new SolidColorBrush(Color.FromRgb(228, 232, 239));
        accent.Freeze();
        Channels =
        [
            CreateChannel("CH 1", accent),
            CreateChannel("CH 2", accent)
        ];

        BackCommand = new RelayCommand(_ => BackRequested?.Invoke(this, EventArgs.Empty));
        SynchronizedStartCommand = CreateHardwareCommand(_ => StartSynchronizedAsync(), HandleStartFailure);
        StartChannelCommand = new AsyncRelayCommand(
            async (parameter, _) =>
            {
                if (parameter is ChannelConfig channel)
                {
                    await StartChannelAsync(channel);
                }
            },
            onError: HandleStartFailure);
        EmergencyStopCommand = CreateHardwareCommand(_ => EmergencyStopAsync());
    }

    public LocalizationViewModel Localization { get; }

    public ObservableCollection<ChannelConfig> Channels { get; }

    public ICommand BackCommand { get; }

    public ICommand SynchronizedStartCommand { get; }

    public ICommand StartChannelCommand { get; }

    public ICommand EmergencyStopCommand { get; }

    public string AppliedPrescriptionName
    {
        get => appliedPrescriptionName;
        private set => SetProperty(ref appliedPrescriptionName, value);
    }

    public event EventHandler? BackRequested;

    public event EventHandler<HardwareOperationResult>? HardwareOperationCompleted;

    public void ApplyPrescription(PrescriptionDefinition prescription)
    {
        AppliedPrescriptionName = prescription.Name;
        var current = prescription.CurrentMilliamp.ToString("0.##", CultureInfo.InvariantCulture);
        var duration = (prescription.TotalDurationMinutes * 60).ToString(CultureInfo.InvariantCulture);
        var interval = ((prescription.IntervalMinutes ?? 0) * 60).ToString(CultureInfo.InvariantCulture);
        var singleDuration = ((prescription.SessionDurationMinutes ?? prescription.TotalDurationMinutes) * 60)
            .ToString(CultureInfo.InvariantCulture);
        var mode = prescription.DeliveryMode == PrescriptionDeliveryModes.Interval ? "间隔" : "连续";

        for (var channelIndex = 0; channelIndex < Channels.Count; channelIndex++)
        {
            var channel = Channels[channelIndex];
            channel.CurrentMA = current;
            channel.RampUpS = prescription.RampUpSeconds.ToString(CultureInfo.InvariantCulture);
            channel.RampDownS = prescription.RampDownSeconds.ToString(CultureInfo.InvariantCulture);
            channel.DurationS = duration;
            channel.IntervalS = interval;
            channel.SingleDurationS = singleDuration;
            channel.StimulationMode = mode;
            channel.Polarity = prescription.GetChannelPolarity(channelIndex);
        }
    }

    private static ChannelConfig CreateChannel(string name, Brush accent)
    {
        return new ChannelConfig
        {
            Name = name,
            CurrentMA = string.Empty,
            RampUpS = "0.5",
            RampDownS = "0.5",
            DurationS = "1200",
            IntervalS = "0",
            SingleDurationS = "60",
            FrequencyHz = string.Empty,
            Polarity = "不掉转",
            StimulationMode = "间隔",
            AccentBrush = accent
        };
    }

    private ICommand CreateHardwareCommand(Func<object?, Task> execute, Action<Exception>? onError = null)
    {
        return new AsyncRelayCommand(
            async (parameter, _) => await execute(parameter),
            onError: onError ?? (ex => logger.Error("tDCS 控制命令执行失败", ex)));
    }

    private void HandleStartFailure(Exception exception)
    {
        logger.Error("刺激启动失败", exception);
        toastService.ShowError(
            "刺激启动失败",
            "刺激启动命令未完成，软件未进入运行状态。具体原因已记录到运行日志。");
    }

    private async Task StartSynchronizedAsync()
    {
        var group = CreateExecutionGroup(Channels);
        var result = await stimulationEngine.StartDirectCurrentGroupAsync(
            group,
            string.Join(" + ", Channels.Select(channel => channel.Name)),
            AppliedPrescriptionName);
        foreach (var channel in Channels)
        {
            countdown.Start(channel);
        }

        HardwareOperationCompleted?.Invoke(this, result);
    }

    private async Task StartChannelAsync(ChannelConfig channel)
    {
        if (!Channels.Contains(channel))
        {
            return;
        }

        var group = CreateExecutionGroup([channel]);
        var result = await stimulationEngine.StartDirectCurrentGroupAsync(group, channel.Name, AppliedPrescriptionName);
        countdown.Start(channel);
        HardwareOperationCompleted?.Invoke(this, result);
    }

    private async Task EmergencyStopAsync()
    {
        var group = CreateExecutionGroup(Channels);
        var result = await stimulationEngine.EmergencyStopDirectCurrentGroupAsync(group, "用户点击急停");
        countdown.CancelAll(Channels, reset: true);
        HardwareOperationCompleted?.Invoke(this, result);
    }

    private async Task CompleteChannelAsync(ChannelConfig channel)
    {
        try
        {
            if (!Channels.Contains(channel))
            {
                return;
            }

            var group = CreateExecutionGroup([channel]);
            var result = await stimulationEngine.CompleteGroupAsync(group, channel.Name, "tDCS");
            HardwareOperationCompleted?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            logger.Error($"tDCS 通道 {channel.Name} 完成记录失败", ex);
        }
    }

    private static TiGroup CreateExecutionGroup(IEnumerable<ChannelConfig> channels)
    {
        var group = new TiGroup { Title = "经颅直流电刺激", DeltaText = string.Empty };
        foreach (var channel in channels)
        {
            group.Channels.Add(channel);
        }

        return group;
    }
}
