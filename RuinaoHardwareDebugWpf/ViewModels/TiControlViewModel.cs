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
    private TiGroup? selectedGroup;
    private TiGroup? lastSelectedGroup;

    public TiControlViewModel(
        IStimulationEngine stimulationEngine,
        ILoggingService logger,
        ITiGroupFactory tiGroupFactory,
        LocalizationViewModel localization)
    {
        this.stimulationEngine = stimulationEngine;
        this.logger = logger;
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
        PauseCommand = CreateHardwareCommand(_ => PauseSelectedGroupAsync());
        EmergencyStopCommand = CreateHardwareCommand(_ => EmergencyStopSelectedGroupAsync());

        SelectedGroup = Groups.FirstOrDefault();
        lastSelectedGroup = SelectedGroup;
    }

    /// <summary>
    /// 硬件操作完成事件。
    /// MainViewModel 订阅它，用来刷新底部状态栏，避免 TI 页面直接持有 Shell。
    /// </summary>
    public event EventHandler<HardwareOperationResult>? HardwareOperationCompleted;

    public LocalizationViewModel Localization { get; }

    public ObservableCollection<TiGroup> Groups { get; }

    public ICommand SelectGroupCommand { get; }

    public ICommand StartCommand { get; }

    public ICommand PauseCommand { get; }

    public ICommand EmergencyStopCommand { get; }

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

        HardwareOperationCompleted?.Invoke(
            this,
            await stimulationEngine.StartTiGroupAsync(SelectedGroup, SelectedChannelNames));
    }

    private async Task PauseSelectedGroupAsync()
    {
        if (SelectedGroup is null)
        {
            logger.Debug("PROTO PAUSE skipped: no TI group selected");
            return;
        }

        HardwareOperationCompleted?.Invoke(
            this,
            await stimulationEngine.PauseTiGroupAsync(SelectedGroup, SelectedChannelNames));
    }

    private async Task EmergencyStopSelectedGroupAsync()
    {
        if (SelectedGroup is null)
        {
            logger.Debug("PROTO EMERGENCY skipped: no TI group selected");
            return;
        }

        HardwareOperationCompleted?.Invoke(
            this,
            await stimulationEngine.EmergencyStopTiGroupAsync(SelectedGroup, "用户点击急停"));
    }
}
