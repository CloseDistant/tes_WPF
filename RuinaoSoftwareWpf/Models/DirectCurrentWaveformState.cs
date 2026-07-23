using System.Globalization;

namespace RuinaoSoftwareWpf;

public enum DirectCurrentWaveformRunState
{
    Empty,
    Running,
    Completed,
    EmergencyStopped
}

/// <summary>
/// tDCS 模拟波形的参数快照。波形由参数和时间即时计算，不保存历史采样点。
/// </summary>
public sealed record DirectCurrentWaveformParameters(
    double CurrentMilliamp,
    double RampUpSeconds,
    double RampDownSeconds,
    double TotalDurationSeconds,
    double IntervalSeconds,
    double PlateauSeconds,
    bool IsContinuous,
    bool ReversePolarity)
{
    public static bool TryCreate(ChannelConfig channel, out DirectCurrentWaveformParameters? parameters, out string error)
    {
        parameters = null;
        error = string.Empty;

        if (!TryPositive(channel.CurrentMA, out var current))
        {
            error = $"{channel.Name}：幅值必须是大于 0 的数字。";
            return false;
        }

        if (!TryNonNegative(channel.RampUpS, out var rampUp))
        {
            error = $"{channel.Name}：渐升时间必须是大于或等于 0 的数字。";
            return false;
        }

        if (!TryNonNegative(channel.RampDownS, out var rampDown))
        {
            error = $"{channel.Name}：渐降时间必须是大于或等于 0 的数字。";
            return false;
        }

        if (!TryPositive(channel.DurationS, out var totalDuration))
        {
            error = $"{channel.Name}：刺激时间必须是大于 0 的数字。";
            return false;
        }

        var continuous = channel.IsContinuousMode;
        var interval = 0d;
        var plateau = 0d;
        if (continuous)
        {
            if (rampUp + rampDown > totalDuration)
            {
                error = $"{channel.Name}：刺激时间不能小于渐升与渐降时间之和。";
                return false;
            }
        }
        else
        {
            if (!TryNonNegative(channel.IntervalS, out interval))
            {
                error = $"{channel.Name}：间隔时间必须是大于或等于 0 的数字。";
                return false;
            }

            if (!TryPositive(channel.SingleDurationS, out var singleStimulationDuration))
            {
                error = $"{channel.Name}：单次时长必须是大于 0 的数字。";
                return false;
            }

            if (rampUp + rampDown > singleStimulationDuration)
            {
                error = $"{channel.Name}：单次时长已包含渐升和渐降，不能小于二者之和。";
                return false;
            }

            if (rampUp + rampDown > totalDuration)
            {
                error = $"{channel.Name}：刺激时间不足以完成一次渐升和渐降。";
                return false;
            }

            // 间隔模式的“单次时长”是从渐升开始到渐降结束的完整刺激段，
            // 不是恒流平台时长。绘图参数只保存扣除两段斜坡后的平台时间。
            plateau = singleStimulationDuration - rampUp - rampDown;
        }

        parameters = new DirectCurrentWaveformParameters(
            current,
            rampUp,
            rampDown,
            totalDuration,
            interval,
            plateau,
            continuous,
            string.Equals(channel.Polarity, "调转", StringComparison.Ordinal));
        return true;
    }

    private static bool TryPositive(string? text, out double value) => TryFinite(text, out value) && value > 0;

    private static bool TryNonNegative(string? text, out double value) => TryFinite(text, out value) && value >= 0;

    private static bool TryFinite(string? text, out double value)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value);
    }
}

/// <summary>单个 tDCS 通道的轻量波形运行状态。</summary>
public sealed class DirectCurrentWaveformState : ObservableObject
{
    private DirectCurrentWaveformParameters? parameters;
    private DirectCurrentWaveformRunState runState;
    private double elapsedSeconds;
    private bool isGlobalView;

    public DirectCurrentWaveformParameters? Parameters
    {
        get => parameters;
        private set => SetProperty(ref parameters, value);
    }

    public DirectCurrentWaveformRunState RunState
    {
        get => runState;
        private set
        {
            if (SetProperty(ref runState, value))
            {
                OnPropertyChanged(nameof(HasWaveform));
                OnPropertyChanged(nameof(IsRunning));
            }
        }
    }

    public double ElapsedSeconds
    {
        get => elapsedSeconds;
        private set => SetProperty(ref elapsedSeconds, value);
    }

    public bool IsGlobalView
    {
        get => isGlobalView;
        set
        {
            if (SetProperty(ref isGlobalView, value))
            {
                OnPropertyChanged(nameof(ViewModeText));
                OnPropertyChanged(nameof(IsWindowView));
            }
        }
    }

    public bool HasWaveform => RunState != DirectCurrentWaveformRunState.Empty && Parameters is not null;

    public bool IsRunning => RunState == DirectCurrentWaveformRunState.Running;

    public string ViewModeText => IsGlobalView ? "全程" : "120 s";

    public bool IsWindowView
    {
        get => !IsGlobalView;
        set
        {
            if (value)
            {
                IsGlobalView = false;
            }
        }
    }

    public void Start(DirectCurrentWaveformParameters snapshot)
    {
        Parameters = snapshot;
        ElapsedSeconds = 0;
        RunState = DirectCurrentWaveformRunState.Running;
    }

    public void UpdateElapsed(double elapsed)
    {
        if (RunState != DirectCurrentWaveformRunState.Running || Parameters is null)
        {
            return;
        }

        ElapsedSeconds = Math.Clamp(elapsed, 0, Parameters.TotalDurationSeconds);
    }

    public void Complete()
    {
        if (Parameters is null)
        {
            return;
        }

        ElapsedSeconds = Parameters.TotalDurationSeconds;
        RunState = DirectCurrentWaveformRunState.Completed;
    }

    public void EmergencyStop(double elapsed)
    {
        if (Parameters is null)
        {
            return;
        }

        ElapsedSeconds = Math.Clamp(elapsed, 0, Parameters.TotalDurationSeconds);
        RunState = DirectCurrentWaveformRunState.EmergencyStopped;
    }

    public void ToggleViewMode() => IsGlobalView = !IsGlobalView;
}
