namespace RuinaoSoftwareWpf;

public enum PulseCurrentWaveformRunState
{
    Empty,
    Running,
    Completed,
    EmergencyStopped
}

/// <summary>
/// 单个 tPCS 通道的轻量模拟波形状态。仅保存参数快照和时间，不缓存历史采样点。
/// </summary>
public sealed class PulseCurrentWaveformState : ObservableObject
{
    private PulseCurrentParameters? parameters;
    private PulseCurrentWaveformRunState runState;
    private double elapsedSeconds;
    private bool isGlobalView;

    public PulseCurrentParameters? Parameters
    {
        get => parameters;
        private set => SetProperty(ref parameters, value);
    }

    public PulseCurrentWaveformRunState RunState
    {
        get => runState;
        private set
        {
            if (!SetProperty(ref runState, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasWaveform));
            OnPropertyChanged(nameof(IsRunning));
        }
    }

    public double ElapsedSeconds
    {
        get => elapsedSeconds;
        private set
        {
            if (!SetProperty(ref elapsedSeconds, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CompletedPulseCount));
            OnPropertyChanged(nameof(PulseCountDisplay));
        }
    }

    public bool HasWaveform => RunState != PulseCurrentWaveformRunState.Empty && Parameters is not null;

    public bool IsRunning => RunState == PulseCurrentWaveformRunState.Running;

    public bool IsGlobalView
    {
        get => isGlobalView;
        set
        {
            if (SetProperty(ref isGlobalView, value))
            {
                OnPropertyChanged(nameof(IsWindowView));
            }
        }
    }

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

    public long CompletedPulseCount => Parameters is null
        ? 0
        : PulseCurrentWaveformMath.GetCompletedPulseCount(Parameters, ElapsedSeconds);

    public string PulseCountDisplay => Parameters is null
        ? string.Empty
        : $"当前次数  {CompletedPulseCount} / {Parameters.PlannedTotalCount}";

    public void Start(PulseCurrentParameters snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Parameters = snapshot;
        ElapsedSeconds = 0;
        RunState = PulseCurrentWaveformRunState.Running;
        OnPropertyChanged(nameof(PulseCountDisplay));
    }

    public void UpdateElapsed(double elapsed)
    {
        if (RunState != PulseCurrentWaveformRunState.Running || Parameters is null)
        {
            return;
        }

        ElapsedSeconds = Math.Clamp(elapsed, 0, Parameters.TreatmentDurationSeconds);
    }

    public void Complete()
    {
        if (Parameters is null)
        {
            return;
        }

        ElapsedSeconds = Parameters.TreatmentDurationSeconds;
        RunState = PulseCurrentWaveformRunState.Completed;
    }

    public void EmergencyStop(double elapsed)
    {
        if (Parameters is null)
        {
            return;
        }

        ElapsedSeconds = Math.Clamp(elapsed, 0, Parameters.TreatmentDurationSeconds);
        RunState = PulseCurrentWaveformRunState.EmergencyStopped;
    }

    public void Clear()
    {
        Parameters = null;
        ElapsedSeconds = 0;
        RunState = PulseCurrentWaveformRunState.Empty;
        OnPropertyChanged(nameof(PulseCountDisplay));
    }
}

/// <summary>tPCS 参数波形的确定性计算规则。</summary>
public static class PulseCurrentWaveformMath
{
    public static double GetSimulatedCurrent(PulseCurrentParameters parameters, double seconds)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (seconds < 0 || seconds > parameters.TreatmentDurationSeconds)
        {
            return 0;
        }

        var riseSeconds = parameters.RiseWidthMilliseconds / 1000d;
        var pulseSeconds = parameters.PulseWidthMilliseconds / 1000d;
        var intervalSeconds = parameters.IntervalWidthMilliseconds / 1000d;
        var activeSeconds = riseSeconds + pulseSeconds;
        var cycleSeconds = activeSeconds + intervalSeconds;
        if (pulseSeconds <= 0 || cycleSeconds <= 0)
        {
            return 0;
        }

        var cycleIndex = (long)Math.Floor(seconds / cycleSeconds);
        if (cycleIndex >= parameters.PlannedTotalCount)
        {
            return 0;
        }

        var localSeconds = seconds - cycleIndex * cycleSeconds;
        var signedCurrent = string.Equals(
            parameters.Polarity,
            PulseCurrentPolarities.Reversed,
            StringComparison.Ordinal)
            ? -parameters.CurrentMilliamp
            : parameters.CurrentMilliamp;

        // 上升宽度为 0 ms 时，脉冲起点直接垂直跳到目标幅值。
        if (riseSeconds > 0 && localSeconds < riseSeconds)
        {
            return signedCurrent * localSeconds / riseSeconds;
        }

        return localSeconds < activeSeconds ? signedCurrent : 0;
    }

    public static long GetCompletedPulseCount(PulseCurrentParameters parameters, double elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var riseSeconds = parameters.RiseWidthMilliseconds / 1000d;
        var pulseSeconds = parameters.PulseWidthMilliseconds / 1000d;
        var intervalSeconds = parameters.IntervalWidthMilliseconds / 1000d;
        var activeSeconds = riseSeconds + pulseSeconds;
        var cycleSeconds = activeSeconds + intervalSeconds;
        if (elapsedSeconds < activeSeconds || cycleSeconds <= 0)
        {
            return 0;
        }

        var completed = (long)Math.Floor((elapsedSeconds - activeSeconds) / cycleSeconds) + 1;
        return Math.Clamp(completed, 0, parameters.PlannedTotalCount);
    }
}
