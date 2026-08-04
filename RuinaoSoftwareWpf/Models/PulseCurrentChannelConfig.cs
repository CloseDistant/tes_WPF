using System.Globalization;

using System.Windows.Media;

namespace RuinaoSoftwareWpf;

/// <summary>
/// 单个 tPCS 通道的界面参数。硬件协议接入前仅作为本次登录期间的编辑状态。
/// </summary>
public sealed class PulseCurrentChannelConfig : ObservableObject, IStimulationImpedanceChannel
{
    private string name = string.Empty;
    private string currentMilliamp = PulseCurrentParameterRules.DefaultCurrentMilliamp;
    private string pulseWidthMilliseconds = PulseCurrentParameterRules.DefaultPulseWidthMilliseconds;
    private string riseWidthMilliseconds = PulseCurrentParameterRules.DefaultRiseWidthMilliseconds;
    private string intervalWidthMilliseconds = PulseCurrentParameterRules.DefaultIntervalWidthMilliseconds;
    private string treatmentDurationSeconds = PulseCurrentParameterRules.DefaultTreatmentDurationSeconds;
    private string polarity = PulseCurrentPolarities.NotReversed;
    private string plannedTotalCount = "—";
    private string remainingTime = "00:00:00";
    private bool isParameterEditingEnabled = true;
    private bool isStimulating;
    private bool isSelected;
    private decimal? impedanceOhms;

    public string Name { get => name; set => SetProperty(ref name, value); }

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }

    public string CurrentMilliamp { get => currentMilliamp; set => SetParameter(ref currentMilliamp, value); }

    public string PulseWidthMilliseconds
    {
        get => pulseWidthMilliseconds;
        set => SetParameter(ref pulseWidthMilliseconds, value);
    }

    public string RiseWidthMilliseconds
    {
        get => riseWidthMilliseconds;
        set => SetParameter(ref riseWidthMilliseconds, value);
    }

    public string IntervalWidthMilliseconds
    {
        get => intervalWidthMilliseconds;
        set => SetParameter(ref intervalWidthMilliseconds, value);
    }

    public string TreatmentDurationSeconds
    {
        get => treatmentDurationSeconds;
        set => SetParameter(ref treatmentDurationSeconds, value);
    }

    public string Polarity
    {
        get => polarity;
        set
        {
            // WPF 在页面退出、ItemsSource 解除绑定时可能短暂回写 null。
            // 极性属于本次登录期间保留的通道参数，不能被视图生命周期清空。
            if (!PulseCurrentPolarities.All.Contains(value, StringComparer.Ordinal))
            {
                return;
            }

            SetParameter(ref polarity, value);
        }
    }

    public string MonitoringDisplay => "允许";

    public string PlannedTotalCount
    {
        get => plannedTotalCount;
        private set => SetProperty(ref plannedTotalCount, value);
    }

    public string CountThresholdDisplay => string.Empty;

    public decimal? ImpedanceOhms
    {
        get => impedanceOhms;
        private set
        {
            if (SetProperty(ref impedanceOhms, value))
            {
                OnPropertyChanged(nameof(ImpedanceOhm));
                OnPropertyChanged(nameof(ImpedanceStatus));
                OnPropertyChanged(nameof(ImpedanceBrush));
                OnPropertyChanged(nameof(StatusIndicatorBrush));
            }
        }
    }

    public string ImpedanceOhm => ImpedanceOhms?.ToString("0.00", CultureInfo.InvariantCulture) ?? "—";

    public StimulationImpedanceStatus ImpedanceStatus =>
        StimulationImpedancePresentation.GetStatus(ImpedanceOhms);

    public Brush ImpedanceBrush =>
        StimulationImpedancePresentation.GetImpedanceBrush(ImpedanceStatus);

    public Brush StatusIndicatorBrush =>
        StimulationImpedancePresentation.GetStatusIndicatorBrush(ImpedanceStatus, IsStimulating);

    public string RemainingTime
    {
        get => remainingTime;
        set => SetProperty(ref remainingTime, value);
    }

    public bool IsParameterEditingEnabled
    {
        get => isParameterEditingEnabled;
        set => SetProperty(ref isParameterEditingEnabled, value);
    }

    /// <summary>当前通道是否正在执行电刺激，用于驱动运行状态指示灯。</summary>
    public bool IsStimulating
    {
        get => isStimulating;
        set
        {
            if (SetProperty(ref isStimulating, value))
            {
                OnPropertyChanged(nameof(StatusIndicatorBrush));
            }
        }
    }

    public PulseCurrentWaveformState Waveform { get; } = new();

    public void ShowPlannedTotalCount(long totalCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalCount);
        PlannedTotalCount = totalCount.ToString(CultureInfo.InvariantCulture);
    }

    public void ClearPlannedTotalCount()
    {
        PlannedTotalCount = "—";
    }

    /// <summary>批量应用处方后通知当前可见卡片重新读取全部绑定值。</summary>
    internal void RefreshBindings()
    {
        OnPropertyChanged(string.Empty);
    }

    internal void UpdateImpedance(decimal? value)
    {
        ImpedanceOhms = value;
    }

    private void SetParameter(ref string field, string value)
    {
        if (SetProperty(ref field, value))
        {
            ClearPlannedTotalCount();
        }
    }
}
