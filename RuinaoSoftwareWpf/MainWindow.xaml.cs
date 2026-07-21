using System.Windows;

namespace RuinaoSoftwareWpf;

using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Threading;

/// <summary>
/// 主窗口的后台代码（Code-Behind）。
///
/// 在 MVVM 模式里，这里应该尽量“薄”：
/// - 只负责创建主 ViewModel 并把它交给 DataContext。
/// - 具体业务逻辑都写在 MainViewModel 里，不要在这里写硬件通讯细节。
/// </summary>
public partial class MainWindow : Window
{
    // 通过 DI 容器创建主 ViewModel。
    private readonly MainViewModel viewModel = AppComposition.CreateMainViewModel();
    private readonly ILoggingService logger = (ILoggingService)AppComposition.Services.GetService(typeof(ILoggingService))!;
    private readonly IRuntimeTelemetryService telemetry = (IRuntimeTelemetryService)AppComposition.Services.GetService(typeof(IRuntimeTelemetryService))!;
    private readonly IAccountService accountService = (IAccountService)AppComposition.Services.GetService(typeof(IAccountService))!;
    private readonly IAuditTrailService auditTrail = (IAuditTrailService)AppComposition.Services.GetService(typeof(IAuditTrailService))!;
    private readonly ISoftwareActivationService softwareActivationService = (ISoftwareActivationService)AppComposition.Services.GetService(typeof(ISoftwareActivationService))!;
    private long lastRenderTicks;
    private bool closeAfterShutdown;
    private bool shutdownInProgress;
    private bool shutdownRequested;
    private readonly CancellationTokenSource automaticConnectionCts = new();

    internal bool IsShutdownRequested => shutdownRequested || shutdownInProgress || closeAfterShutdown;

    public MainWindow()
    {
        logger.Info("开始加载主窗口 XAML");
        InitializeComponent();
        logger.Info("主窗口 XAML 加载完成");

        // 把 ViewModel 设为窗口的数据上下文，XAML 里的绑定才能找到属性。
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
        auditTrail.WriteFailed += OnAuditTrailWriteFailed;
        RegisterAuthenticationEvents();
        RegisterSessionSecurity();
        ContentRendered += OnFirstContentRendered;
        Closing += OnClosing;
        CompositionTarget.Rendering += OnRendering;
        Closed += (_, _) =>
        {
            CompositionTarget.Rendering -= OnRendering;
            viewModel.CloseRequested -= OnCloseRequested;
            auditTrail.WriteFailed -= OnAuditTrailWriteFailed;
            UnregisterAuthenticationEvents();
            UnregisterSessionSecurity();
            automaticConnectionCts.Dispose();
        };
        logger.Info("主窗口创建完成");
    }

    private void OnAuditTrailWriteFailed(object? sender, AuditTrailWriteFailedEventArgs e)
    {
        _ = Dispatcher.BeginInvoke(() =>
            viewModel.Toast.ShowError("安全审计异常", e.UserMessage));
    }

    private async void OnFirstContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= OnFirstContentRendered;
        try
        {
            // 未激活时不初始化硬件联机；激活成功后由认证流程重新触发一次检查。
            await softwareActivationService.InitializeAsync(automaticConnectionCts.Token);
            if (!softwareActivationService.IsActivated)
            {
                return;
            }

            await viewModel.TryAutomaticConnectionOnceAsync(automaticConnectionCts.Token);
        }
        catch (OperationCanceledException)
        {
            // 软件关闭时取消自动联机属于正常流程。
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        var previous = Interlocked.Exchange(ref lastRenderTicks, now);
        if (previous != 0)
        {
            telemetry.RecordUiFrame(Stopwatch.GetElapsedTime(previous, now));
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        if (IsShutdownRequested)
        {
            return;
        }

        shutdownRequested = true;
        logger.Info("收到退出软件命令，关闭主窗口");
        MainContent.CloseTransientPopups();

        // 退出按钮位于 Popup 内，等待当前鼠标输入事件结束后再关闭窗口。
        _ = Dispatcher.BeginInvoke(new Action(Close), DispatcherPriority.ContextIdle);
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!IsShutdownRequested
            && ActivationContent.Visibility == Visibility.Visible
            && !softwareActivationService.IsActivated)
        {
            e.Cancel = true;
            ActivationContent.ShowExitConfirmation();
            return;
        }

        shutdownRequested = true;
        automaticConnectionCts.Cancel();
        MainContent.CloseTransientPopups();

        if (closeAfterShutdown)
        {
            return;
        }

        e.Cancel = true;
        if (shutdownInProgress)
        {
            return;
        }

        shutdownInProgress = true;
        IsEnabled = false;
        try
        {
            logger.Info("主窗口正在执行统一关闭流程");
            await viewModel.ShutdownAsync();
            await accountService.LogoutAsync();
        }
        catch (Exception exception)
        {
            logger.Error("统一关闭流程失败，将继续关闭窗口", exception);
        }
        finally
        {
            closeAfterShutdown = true;
            shutdownInProgress = false;
            IsEnabled = true;
            _ = Dispatcher.BeginInvoke(new Action(Close), DispatcherPriority.ApplicationIdle);
        }
    }
}
