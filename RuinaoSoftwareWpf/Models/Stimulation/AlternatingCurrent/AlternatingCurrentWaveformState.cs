namespace RuinaoSoftwareWpf;

public enum AlternatingCurrentWaveformRunState
{
    Empty,
    Running,
    Completed,
    Stopped,
}

/// <summary>单个交流电刺激通道的轻量模拟波形运行状态，不缓存历史采样点。</summary>
public sealed class AlternatingCurrentWaveformState : ObservableObject
{
    private AlternatingCurrentWaveformPreview? preview;
    private AlternatingCurrentWaveformRunState runState;
    private double elapsedSeconds;
    private bool isGlobalView;

    public AlternatingCurrentWaveformPreview? Preview
    {
        get => preview;
        private set => SetProperty(ref preview, value);
    }

    public AlternatingCurrentWaveformRunState RunState
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

    public bool HasWaveform => RunState != AlternatingCurrentWaveformRunState.Empty && Preview is not null;

    public bool IsRunning => RunState == AlternatingCurrentWaveformRunState.Running;

    public void Start(AlternatingCurrentWaveformPreview snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Preview = snapshot;
        ElapsedSeconds = 0;
        RunState = AlternatingCurrentWaveformRunState.Running;
    }

    public void UpdateElapsed(double elapsedSeconds)
    {
        if (!IsRunning || Preview is null)
        {
            return;
        }

        ElapsedSeconds = Math.Clamp(elapsedSeconds, 0, Preview.TotalDurationSeconds);
    }

    public void Complete()
    {
        if (Preview is null)
        {
            return;
        }

        ElapsedSeconds = Preview.TotalDurationSeconds;
        RunState = AlternatingCurrentWaveformRunState.Completed;
    }

    public void Stop(double elapsedSeconds)
    {
        if (Preview is null)
        {
            return;
        }

        ElapsedSeconds = Math.Clamp(elapsedSeconds, 0, Preview.TotalDurationSeconds);
        RunState = AlternatingCurrentWaveformRunState.Stopped;
    }

    public void Clear()
    {
        Preview = null;
        ElapsedSeconds = 0;
        RunState = AlternatingCurrentWaveformRunState.Empty;
    }
}
