using System.Windows;

namespace RuinaoHardwareDebugWpf;

using System.ComponentModel;
using System.Windows.Threading;
using System.Diagnostics;
using System.Windows.Media;

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
    private long lastRenderTicks;
    private bool closeAfterShutdown;
    private bool shutdownInProgress;

    public MainWindow()
    {
        InitializeComponent();

        // 把 ViewModel 设为窗口的数据上下文，XAML 里的绑定才能找到属性。
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
        Closing += OnClosing;
        CompositionTarget.Rendering += OnRendering;
        Closed += (_, _) =>
        {
            CompositionTarget.Rendering -= OnRendering;
            viewModel.CloseRequested -= OnCloseRequested;
        };
        logger.Info("主窗口创建完成");
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
        logger.Info("收到退出软件命令，关闭主窗口");
        Close();
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
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
