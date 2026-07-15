using System.Collections.ObjectModel;
using System.Windows.Input;

namespace RuinaoHardwareDebugWpf;

/// <summary>
/// TI 控制页面 ViewModel。
///
/// 负责维护 TI 刺激组列表、当前选中组，以及开始/暂停/急停等页面级命令。
/// Shell 只负责展示该页面，不再持有 TI 控制页的业务状态。
/// </summary>
public sealed class TiControlViewModel : ObservableObject
{
    private readonly IStimulationEngine stimulationEngine;
    private readonly ILoggingService logger;
    private readonly StimulationChannelCountdown countdown = new();
    private TiGroup? selectedGroup;
    private TiGroup? lastSelectedGroup;
    private string appliedPrescriptionName = "手动设置";
    private string deliveryMode = PrescriptionDeliveryModes.Continuous;
    private int totalDurationMinutes = 20;
    private int? intervalMinutes;
    private int? sessionDurationMinutes;

    public TiControlViewModel(
        IStimulationEngine stimulationEngine,
        ILoggingService logger,
        ITiGroupFactory tiGroupFactory,
        LocalizationViewModel localization)
    {
        this.stimulationEngine = stimulationEngine;
        this.logger = logger;
        countdown.Completed += channel => _ = CompleteChannelAsync(channel);
        Localization = localization;
        Groups = new ObservableCollection<TiGroup>(tiGroupFactory.CreateDemoGroups());

        SelectGroupCommand = new RelayCommand(parameter =>
        {
            if (parameter is TiGroup group)
            {
                SelectedGroup = group;
            }
        });

        StartCommand = CreateHardwareCommand(_ => StartSelectedGroupAsync());
        StartChannelCommand = new AsyncRelayCommand(
            async (parameter, _) =>
            {
                if (parameter is ChannelConfig channel)
                {
                    await StartChannelAsync(channel);
                }
            },
            onError: ex => logger.Error("TI 单通道启动失败", ex));
        PauseCommand = CreateHardwareCommand(_ => PauseSelectedGroupAsync());
        EmergencyStopCommand = CreateHardwareCommand(_ => EmergencyStopSelectedGroupAsync());
        BackCommand = new RelayCommand(_ => BackRequested?.Invoke(this, EventArgs.Empty));

        SelectedGroup = Groups.FirstOrDefault();
        lastSelectedGroup = SelectedGroup;
    }

    /// <summary>
    /// 硬件操作完成事件。
    /// MainViewModel 订阅它，用来刷新底部状态栏，避免 TI 页面直接持有 Shell。
    /// </summary>
    public event EventHandler<HardwareOperationResult>? HardwareOperationCompleted;

    /// <summary>请求返回电刺激类型选择页。</summary>
    public event EventHandler? BackRequested;

    public LocalizationViewModel Localization { get; }

    public ObservableCollection<TiGroup> Groups { get; }

    public ICommand SelectGroupCommand { get; }

    public ICommand StartCommand { get; }

    public ICommand StartChannelCommand { get; }

    public ICommand PauseCommand { get; }

    public ICommand EmergencyStopCommand { get; }

    public ICommand BackCommand { get; }
    public string AppliedPrescriptionName { get => appliedPrescriptionName; private set => SetProperty(ref appliedPrescriptionName, value); }
    public string DeliveryMode { get => deliveryMode; private set => SetProperty(ref deliveryMode, value); }
    public int TotalDurationMinutes { get => totalDurationMinutes; private set => SetProperty(ref totalDurationMinutes, value); }
    public int? IntervalMinutes { get => intervalMinutes; private set => SetProperty(ref intervalMinutes, value); }
    public int? SessionDurationMinutes { get => sessionDurationMinutes; private set => SetProperty(ref sessionDurationMinutes, value); }

    public bool IsStimulationRunning => stimulationEngine.CurrentState == StimulationExecutionState.Running;

    public TiGroup? SelectedGroup
    {
        get => selectedGroup;
        set
        {
            if (SetProperty(ref selectedGroup, value))
            {
                foreach (var group in Groups)
                {
                    group.IsSelected = ReferenceEquals(group, value);
                }

                OnPropertyChanged(nameof(SelectedChannelNames));

                if (value is not null)
                {
                    lastSelectedGroup = value;
                    logger.Debug($"SELECT {value.Title} -> show only {SelectedChannelNames}");
                }
            }
        }
    }

    public string SelectedChannelNames =>
        SelectedGroup is null ? string.Empty : string.Join(" + ", SelectedGroup.Channels.Select(c => c.Name));

    /// <summary>
    /// 回到 TI 控制页时恢复上一次选择；如果没有历史选择，则默认选中第一组。
    /// </summary>
    public void RestoreLastSelection()
    {
        if (lastSelectedGroup is not null && Groups.Contains(lastSelectedGroup))
        {
            SelectedGroup = lastSelectedGroup;
            return;
        }

        SelectedGroup = Groups.FirstOrDefault();
    }

    public void ApplyPrescription(PrescriptionDefinition prescription)
    {
        AppliedPrescriptionName = prescription.Name;
        DeliveryMode = prescription.DeliveryMode;
        TotalDurationMinutes = prescription.TotalDurationMinutes;
        IntervalMinutes = prescription.IntervalMinutes;
        SessionDurationMinutes = prescription.SessionDurationMinutes;
        var current = prescription.CurrentMilliamp.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        var durationSeconds = (prescription.TotalDurationMinutes * 60).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var singleDurationSeconds = ((prescription.SessionDurationMinutes ?? prescription.TotalDurationMinutes) * 60)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var group in Groups)
        {
            for (var channelIndex = 0; channelIndex < group.Channels.Count; channelIndex++)
            {
                var channel = group.Channels[channelIndex];
                channel.CurrentMA = current;
                channel.RampUpS = prescription.RampUpSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                channel.RampDownS = prescription.RampDownSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                channel.DurationS = durationSeconds;
                channel.SingleDurationS = singleDurationSeconds;
                channel.Polarity = prescription.GetChannelPolarity(channelIndex);
            }
        }
    }

    private ICommand CreateHardwareCommand(Func<object?, Task> execute)
    {
        return new AsyncRelayCommand(
            async (parameter, _) => await execute(parameter),
            onError: ex => logger.Error("TI 控制命令执行失败", ex));
    }

    private async Task StartSelectedGroupAsync()
    {
        if (SelectedGroup is null)
        {
            logger.Debug("PROTO START skipped: no TI group selected");
            return;
        }

        var result = await stimulationEngine.StartTiGroupAsync(
            SelectedGroup,
            SelectedChannelNames,
            AppliedPrescriptionName);
        foreach (var channel in SelectedGroup.Channels)
        {
            countdown.Start(channel);
        }

        HardwareOperationCompleted?.Invoke(this, result);
    }

    private async Task StartChannelAsync(ChannelConfig channel)
    {
        if (SelectedGroup is null || !SelectedGroup.Channels.Contains(channel))
        {
            logger.Debug("PROTO START channel skipped: channel is not in selected TI group");
            return;
        }

        var singleChannelGroup = new TiGroup
        {
            Title = SelectedGroup.Title,
            DeltaText = SelectedGroup.DeltaText
        };
        singleChannelGroup.Channels.Add(channel);

        var result = await stimulationEngine.StartTiGroupAsync(
            singleChannelGroup,
            channel.Name,
            AppliedPrescriptionName);
        countdown.Start(channel);
        HardwareOperationCompleted?.Invoke(this, result);
    }

    private async Task PauseSelectedGroupAsync()
    {
        if (SelectedGroup is null)
        {
            logger.Debug("PROTO PAUSE skipped: no TI group selected");
            return;
        }

        var result = await stimulationEngine.PauseTiGroupAsync(SelectedGroup, SelectedChannelNames);
        countdown.CancelAll(SelectedGroup.Channels, reset: false);
        HardwareOperationCompleted?.Invoke(this, result);
    }

    private async Task EmergencyStopSelectedGroupAsync()
    {
        if (SelectedGroup is null)
        {
            logger.Debug("PROTO EMERGENCY skipped: no TI group selected");
            return;
        }

        var result = await stimulationEngine.EmergencyStopTiGroupAsync(SelectedGroup, "用户点击急停");
        countdown.CancelAll(Groups.SelectMany(group => group.Channels), reset: true);
        HardwareOperationCompleted?.Invoke(this, result);
    }

    private async Task CompleteChannelAsync(ChannelConfig channel)
    {
        try
        {
            var owner = Groups.FirstOrDefault(group => group.Channels.Contains(channel));
            if (owner is null)
            {
                return;
            }

            var singleChannelGroup = new TiGroup { Title = owner.Title, DeltaText = owner.DeltaText };
            singleChannelGroup.Channels.Add(channel);
            var result = await stimulationEngine.CompleteGroupAsync(
                singleChannelGroup,
                channel.Name,
                "TI");
            HardwareOperationCompleted?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            logger.Error($"TI 通道 {channel.Name} 完成记录失败", ex);
        }
    }
}
