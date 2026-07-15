using System.Windows.Media;

namespace RuinaoHardwareDebugWpf;

/// <summary>
/// 单个硬件通道在界面上的配置与状态。
///
/// 目前字段以字符串保存，方便联调阶段直接输入任意值；
/// 接真实设备前，应再做数值校验与单位转换（例如把 "0.90" 转成 double 0.9 mA）。
/// </summary>
public sealed class ChannelConfig : ObservableObject
{
    private string name = string.Empty;
    private string anode = string.Empty;
    private string cathode = string.Empty;
    private string currentMA = string.Empty;
    private string rampUpS = string.Empty;
    private string rampDownS = string.Empty;
    private string durationS = string.Empty;
    private string intervalS = string.Empty;
    private string singleDurationS = "60";
    private string frequencyHz = string.Empty;
    private string polarity = "不掉转";
    private string stimulationMode = "间隔";
    private string remainingTime = "00:00:00";
    private Brush accentBrush = Brushes.White;

    /// <summary>通道名称，例如 "CH 13"。</summary>
    public string Name { get => name; set => SetProperty(ref name, value); }

    /// <summary>阳极电极编号，例如 "E25"。</summary>
    public string Anode { get => anode; set => SetProperty(ref anode, value); }

    /// <summary>阴极电极编号，例如 "E26"。</summary>
    public string Cathode { get => cathode; set => SetProperty(ref cathode, value); }

    /// <summary>电流幅值，单位 mA。例如 "0.90"。</summary>
    public string CurrentMA { get => currentMA; set => SetProperty(ref currentMA, value); }

    /// <summary>渐升时间，单位秒。</summary>
    public string RampUpS { get => rampUpS; set => SetProperty(ref rampUpS, value); }

    /// <summary>渐降时间，单位秒。</summary>
    public string RampDownS { get => rampDownS; set => SetProperty(ref rampDownS, value); }

    /// <summary>刺激持续时间，单位秒。</summary>
    public string DurationS { get => durationS; set => SetProperty(ref durationS, value); }

    /// <summary>间隔时间，单位秒。仅在间隔模式下有意义。</summary>
    public string IntervalS
    {
        get => intervalS;
        set
        {
            if (SetProperty(ref intervalS, value))
            {
                OnPropertyChanged(nameof(IntervalDisplay));
            }
        }
    }

    /// <summary>间隔模式下每次刺激持续时间，单位秒。</summary>
    public string SingleDurationS
    {
        get => singleDurationS;
        set
        {
            if (SetProperty(ref singleDurationS, value))
            {
                OnPropertyChanged(nameof(SingleDurationDisplay));
            }
        }
    }

    /// <summary>连续模式显示“/”，间隔模式显示并编辑真实间隔时间。</summary>
    public string IntervalDisplay
    {
        get => IsContinuousMode ? "/" : IntervalS;
        set
        {
            if (!IsContinuousMode)
            {
                IntervalS = value;
            }
        }
    }

    /// <summary>连续模式显示“/”，间隔模式显示并编辑真实单次时长。</summary>
    public string SingleDurationDisplay
    {
        get => IsContinuousMode ? "/" : SingleDurationS;
        set
        {
            if (!IsContinuousMode)
            {
                SingleDurationS = value;
            }
        }
    }

    public bool AreIntervalFieldsEnabled => !IsContinuousMode;

    /// <summary>载波频率，单位 Hz。</summary>
    public string FrequencyHz { get => frequencyHz; set => SetProperty(ref frequencyHz, value); }

    /// <summary>刺激期间是否调转极性："不掉转" 或 "调转"。</summary>
    public string Polarity { get => polarity; set => SetProperty(ref polarity, value); }

    /// <summary>
    /// 刺激模式："间隔" 或 "连续"。
    /// 改变它时会联动更新 IsIntervalMode / IsContinuousMode。
    /// </summary>
    public string StimulationMode
    {
        get => stimulationMode;
        set
        {
            if (SetProperty(ref stimulationMode, value))
            {
                OnPropertyChanged(nameof(IsIntervalMode));
                OnPropertyChanged(nameof(IsContinuousMode));
                OnPropertyChanged(nameof(AreIntervalFieldsEnabled));
                OnPropertyChanged(nameof(IntervalDisplay));
                OnPropertyChanged(nameof(SingleDurationDisplay));
            }
        }
    }

    /// <summary>当前是否为间隔模式。绑定到单选按钮。</summary>
    public bool IsIntervalMode
    {
        get => StimulationMode == "间隔";
        set
        {
            if (value)
            {
                StimulationMode = "间隔";
            }
        }
    }

    /// <summary>当前是否为连续模式。绑定到单选按钮。</summary>
    public bool IsContinuousMode
    {
        get => StimulationMode == "连续";
        set
        {
            if (value)
            {
                StimulationMode = "连续";
            }
        }
    }

    /// <summary>剩余时间显示，例如 "00:10:00"。</summary>
    public string RemainingTime { get => remainingTime; set => SetProperty(ref remainingTime, value); }

    /// <summary>该通道名称在列表和详情页中的显示颜色。</summary>
    public Brush AccentBrush { get => accentBrush; set => SetProperty(ref accentBrush, value); }

    // 下面三个状态值先用 mock 固定值。接设备后应从 ReadStatus/ReadImpedance 返回值更新。
    public int AnodeTempC => 36;
    public int CathodeTempC => 36;
    public int ImpedanceOhm => 500;
}
