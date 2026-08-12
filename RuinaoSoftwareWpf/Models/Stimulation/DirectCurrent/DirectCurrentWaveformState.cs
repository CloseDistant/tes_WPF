using System.Globalization;

namespace RuinaoSoftwareWpf;

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

    public string ViewModeText => IsGlobalView ? "全程" : "波形细节";

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

    public void Clear()
    {
        Parameters = null;
        ElapsedSeconds = 0;
        RunState = DirectCurrentWaveformRunState.Empty;
    }

    public void ToggleViewMode() => IsGlobalView = !IsGlobalView;
}
