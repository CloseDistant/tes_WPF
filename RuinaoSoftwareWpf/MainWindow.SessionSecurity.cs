namespace RuinaoSoftwareWpf;

using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Threading;

public partial class MainWindow
{
    private readonly ISessionSecurityService sessionSecurityService =
        AppComposition.Services.GetRequiredService<ISessionSecurityService>();
    private readonly GlobalUserActivityMonitor globalUserActivityMonitor =
        AppComposition.Services.GetRequiredService<GlobalUserActivityMonitor>();
    private readonly SessionLockViewModel sessionLockViewModel =
        AppComposition.Services.GetRequiredService<SessionLockViewModel>();
    private readonly DispatcherTimer sessionSecurityTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };
    private int sessionSecurityStarted;

    private void RegisterSessionSecurity()
    {
        SessionLockContent.DataContext = sessionLockViewModel;
        SessionLockContent.ExitRequested += OnCloseRequested;
        sessionSecurityService.StateChanged += OnSessionSecurityStateChanged;
        sessionSecurityTimer.Tick += OnSessionSecurityTimerTick;
        Loaded += OnSessionSecurityLoaded;
    }

    private void UnregisterSessionSecurity()
    {
        Loaded -= OnSessionSecurityLoaded;
        sessionSecurityTimer.Stop();
        sessionSecurityTimer.Tick -= OnSessionSecurityTimerTick;
        sessionSecurityService.StateChanged -= OnSessionSecurityStateChanged;
        SessionLockContent.ExitRequested -= OnCloseRequested;
        globalUserActivityMonitor.Stop();
    }

    private async void OnSessionSecurityLoaded(object sender, RoutedEventArgs e)
    {
        if (Interlocked.Exchange(ref sessionSecurityStarted, 1) != 0)
        {
            return;
        }

        try
        {
            await sessionSecurityService.InitializeAsync();
            globalUserActivityMonitor.Start();
            sessionSecurityTimer.Start();
        }
        catch (Exception exception)
        {
            Interlocked.Exchange(ref sessionSecurityStarted, 0);
            logger.Error("会话安全服务初始化失败", exception);
        }
    }

    private async void OnSessionSecurityTimerTick(object? sender, EventArgs e)
    {
        if (accountService.CurrentUser is null
            || MainContent.Visibility != Visibility.Visible
            || IsShutdownRequested)
        {
            return;
        }

        try
        {
            await sessionSecurityService.EvaluateIdleTimeoutAsync();
        }
        catch (Exception exception)
        {
            logger.Error("会话安全超时检查失败", exception);
        }
    }

    private void OnSessionSecurityStateChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => OnSessionSecurityStateChanged(sender, e));
            return;
        }

        if (!sessionSecurityService.IsLocked)
        {
            return;
        }

        MainContent.CloseTransientPopups();
        CloseOwnedWindowsForSessionLock();
        SessionLockContent.PrepareForLock();
    }

    private void CloseOwnedWindowsForSessionLock()
    {
        var windows = Application.Current.Windows
            .OfType<Window>()
            .Where(window => !ReferenceEquals(window, this) && window.IsVisible)
            .ToArray();

        foreach (var window in windows)
        {
            try
            {
                window.Close();
            }
            catch (Exception exception)
            {
                logger.Warning($"会话锁定时关闭应用内弹窗失败：{window.GetType().Name}，{exception.Message}");
            }
        }
    }
}
