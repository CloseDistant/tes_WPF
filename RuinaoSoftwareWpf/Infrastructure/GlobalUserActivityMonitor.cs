namespace RuinaoSoftwareWpf;

using System.Windows.Input;

public sealed class GlobalUserActivityMonitor : IDisposable
{
    private const long MouseActivityThrottleMilliseconds = 250;

    private readonly ISessionSecurityService sessionSecurityService;
    private long lastMouseActivityTick;
    private bool started;
    private bool disposed;

    public GlobalUserActivityMonitor(ISessionSecurityService sessionSecurityService)
    {
        this.sessionSecurityService = sessionSecurityService;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (started)
        {
            return;
        }

        InputManager.Current.PreProcessInput += OnPreProcessInput;
        started = true;
    }

    public void Stop()
    {
        if (!started)
        {
            return;
        }

        InputManager.Current.PreProcessInput -= OnPreProcessInput;
        started = false;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Stop();
        disposed = true;
    }

    private void OnPreProcessInput(object sender, PreProcessInputEventArgs e)
    {
        var input = e.StagingItem.Input;
        if (input is MouseEventArgs)
        {
            var now = Environment.TickCount64;
            var previous = Interlocked.Read(ref lastMouseActivityTick);
            if (now - previous < MouseActivityThrottleMilliseconds)
            {
                return;
            }

            Interlocked.Exchange(ref lastMouseActivityTick, now);
            sessionSecurityService.NotifyUserActivity();
            return;
        }

        if (input is KeyEventArgs or TextCompositionEventArgs or TouchEventArgs or StylusEventArgs)
        {
            sessionSecurityService.NotifyUserActivity();
        }
    }
}
