using System.Configuration;
using System.Data;
using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace RuinaoSoftwareWpf;

/// <summary>
/// WPF 应用程序入口逻辑（对应 App.xaml）。
///
/// 这里不写界面细节，只处理整个软件生命周期内的事件：
/// - 启动时记录日志路径
/// - 捕获未处理异常，避免程序直接崩溃
/// - 退出时记录 ExitCode
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName =
        @"Local\RuinaoSoftwareWpf.SingleInstance.35D1BCDD-E527-4A59-9085-617606B4FC8E";
    private const string SingleInstancePipeName =
        "RuinaoSoftwareWpf.SingleInstance.Activation.35D1BCDD-E527-4A59-9085-617606B4FC8E";

    private bool systemAwakeInhibitionActive;
    private ILoggingService? logger;
    private SingleInstanceCoordinator? singleInstanceCoordinator;
    private int pendingActivationRequest;

    private ILoggingService Logger =>
        logger ??= AppComposition.GetLoggingService();

    /// <summary>
    /// 软件启动时调用。
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        if (!TryEnterSingleInstance())
        {
            return;
        }

        if (!BackupRestoreRecoveryGuard.TryRecoverPending(out var recoveryMessage))
        {
            MessageBox.Show(
                "检测到未完成的数据恢复，且自动回滚失败。为保护数据，软件将停止启动，请联系维护人员。",
                "数据恢复异常",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-9);
            return;
        }

        Logger.Info("软件启动");
        if (!string.IsNullOrWhiteSpace(recoveryMessage)) Logger.Warning(recoveryMessage);
        Logger.Info($"日志文件：{Logger.CurrentLogPath}");
        var assemblyName = typeof(App).Assembly.GetName();
        Logger.Info(
            $"启动环境：Assembly={assemblyName.Name}; Version={assemblyName.Version}; " +
            $"Runtime={RuntimeInformation.FrameworkDescription}; OS={RuntimeInformation.OSDescription}; " +
            $"ProcessPath={Environment.ProcessPath ?? "未知"}");

        systemAwakeInhibitionActive = SystemSleepInhibitor.TryEnable();
        if (systemAwakeInhibitionActive)
        {
            Logger.Info("已启用软件运行期间的系统防休眠和屏幕常亮请求");
        }
        else
        {
            Logger.Warning("系统防休眠和屏幕常亮请求启用失败，将继续使用Windows当前电源设置");
        }

        // 捕获 UI 线程未处理异常（比如按钮点击里抛出的异常没 try-catch）。
        DispatcherUnhandledException += (_, args) =>
        {
            if (MainWindow is MainWindow { IsShutdownRequested: true }
                && IsPopupShutdownException(args.Exception))
            {
                Logger.Warning("软件关闭阶段已忽略 WPF Popup 鼠标捕获异常。");
                args.Handled = true;
                return;
            }

            Logger.Error("界面线程未处理异常", args.Exception);
            args.Handled = true;

            var fatal = IsFatalException(args.Exception);
            try
            {
                MessageBox.Show(
                    fatal
                        ? "软件发生致命错误。系统将停止当前任务并安全关闭，请根据日志排查原因。"
                        : "当前界面操作发生错误，已记录日志。请关闭当前窗口后重试；若问题重复出现，请联系维护人员。",
                    "软件异常",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
                // 异常提示本身失败时仍继续执行安全关闭。
            }

            if (fatal)
            {
                Dispatcher.BeginInvoke(
                    () => MainWindow?.Close(),
                    DispatcherPriority.Send);
            }
        };

        // 捕获非 UI 线程未处理异常（比如后台任务里的异常）。
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                Logger.Error("应用程序未处理异常", exception);
            }
            else
            {
                Logger.Error($"应用程序未处理异常：{args.ExceptionObject}");
            }
        };

        base.OnStartup(e);

        var mainWindow = AppComposition.CreateMainWindow();
        MainWindow = mainWindow;
        mainWindow.Closing += OnMainWindowClosing;
        mainWindow.Show();
        ActivateMainWindowIfRequested();
    }

    /// <summary>
    /// 软件退出时调用。
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        if (systemAwakeInhibitionActive)
        {
            if (!SystemSleepInhibitor.Disable())
            {
                logger?.Warning("系统防休眠和屏幕常亮请求未能正常解除");
            }

            systemAwakeInhibitionActive = false;
        }

        logger?.Info($"软件退出，ExitCode={e.ApplicationExitCode}");
        singleInstanceCoordinator?.Dispose();
        singleInstanceCoordinator = null;
        base.OnExit(e);
    }

    private bool TryEnterSingleInstance()
    {
        singleInstanceCoordinator = new SingleInstanceCoordinator(
            SingleInstanceMutexName,
            SingleInstancePipeName);
        if (singleInstanceCoordinator.TryAcquireOwnership(TimeSpan.Zero))
        {
            StartActivationListener();
            return true;
        }

        if (TryActivateExistingInstance())
        {
            Shutdown();
            return false;
        }

        var result = MessageBox.Show(
            "软件已在运行，或正在安全关闭。\n\n请选择“重试”等待关闭完成，或选择“取消”退出本次启动。",
            "软件正在运行",
            MessageBoxButton.RetryCancel,
            MessageBoxImage.Information);
        if (result != MessageBoxResult.Retry)
        {
            Shutdown();
            return false;
        }

        if (singleInstanceCoordinator.TryAcquireOwnership(TimeSpan.FromSeconds(10)))
        {
            StartActivationListener();
            return true;
        }

        if (!TryActivateExistingInstance())
        {
            MessageBox.Show(
                "软件仍在运行且暂时没有响应。请返回已打开的软件窗口，或等待其完成安全关闭后再试。\n\n请勿在仪器运行期间强制结束进程。",
                "软件暂时无法启动",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        Shutdown();
        return false;
    }

    private bool TryActivateExistingInstance()
    {
        return singleInstanceCoordinator!
            .TryActivateExistingAsync(TimeSpan.FromSeconds(2))
            .GetAwaiter()
            .GetResult();
    }

    private void StartActivationListener()
    {
        singleInstanceCoordinator!.ActivationRequested += OnActivationRequested;
        singleInstanceCoordinator.StartListening();
    }

    private void OnActivationRequested(object? sender, EventArgs e)
    {
        Interlocked.Exchange(ref pendingActivationRequest, 1);
        _ = Dispatcher.BeginInvoke(
            ActivateMainWindowIfRequested,
            DispatcherPriority.Send);
    }

    private void ActivateMainWindowIfRequested()
    {
        if (Volatile.Read(ref pendingActivationRequest) == 0 || MainWindow is not MainWindow mainWindow)
        {
            return;
        }

        Interlocked.Exchange(ref pendingActivationRequest, 0);
        mainWindow.RestoreAndActivate();
    }

    private void OnMainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (sender is MainWindow { IsShutdownRequested: true })
        {
            singleInstanceCoordinator?.StopListening();
        }
    }

    private static bool IsFatalException(Exception exception)
    {
        return exception is OutOfMemoryException
            or AccessViolationException
            or BadImageFormatException;
    }

    private static bool IsPopupShutdownException(Exception exception)
    {
        if (exception is not NullReferenceException || string.IsNullOrWhiteSpace(exception.StackTrace))
        {
            return false;
        }

        return exception.StackTrace.Contains(
                   "System.Windows.Controls.Primitives.Popup.OnLostMouseCapture",
                   StringComparison.Ordinal)
               && exception.StackTrace.Contains(
                   "System.Windows.Input.StylusWisp",
                   StringComparison.Ordinal);
    }
}
