namespace RuinaoSoftwareWpf;

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

        ElapsedSeconds = Math.Clamp(elapsed, 0, Parameters.TotalRuntimeSeconds);
    }

    public void Complete()
    {
        if (Parameters is null)
        {
            return;
        }

        ElapsedSeconds = Parameters.TotalRuntimeSeconds;
        RunState = PulseCurrentWaveformRunState.Completed;
    }

    public void EmergencyStop(double elapsed)
    {
        if (Parameters is null)
        {
            return;
        }

        ElapsedSeconds = Math.Clamp(elapsed, 0, Parameters.TotalRuntimeSeconds);
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
