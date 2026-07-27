using System.Globalization;

namespace RuinaoSoftwareWpf;

/// <summary>
/// 单个 tPCS 通道的界面参数。硬件协议接入前仅作为本次登录期间的编辑状态。
/// </summary>
public sealed class PulseCurrentChannelConfig : ObservableObject
{
    private string name = string.Empty;
    private string currentMilliamp = string.Empty;
    private string pulseWidthMilliseconds = "10";
    private string riseWidthMilliseconds = "5";
    private string intervalWidthMilliseconds = "20";
    private string treatmentDurationSeconds = "1200";
    private string polarity = PulseCurrentPolarities.NotReversed;
    private string plannedTotalCount = "—";
    private string remainingTime = "00:00:00";
    private bool isParameterEditingEnabled = true;
    private bool isStimulating;
    private bool isSelected;

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

    public string Polarity { get => polarity; set => SetParameter(ref polarity, value); }

    public string MonitoringDisplay => "允许";

    public string PlannedTotalCount
    {
        get => plannedTotalCount;
        private set => SetProperty(ref plannedTotalCount, value);
    }

    public string CountThresholdDisplay => string.Empty;

    public int ImpedanceOhm => 500;

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
        set => SetProperty(ref isStimulating, value);
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

    private void SetParameter(ref string field, string value)
    {
        if (SetProperty(ref field, value))
        {
            ClearPlannedTotalCount();
        }
    }
}
