using System.Configuration;
using System.Data;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
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
    private ILoggingService Logger => AppComposition.Services.GetRequiredService<ILoggingService>();

    /// <summary>
    /// 软件启动时调用。
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        Logger.Info("软件启动");
        Logger.Info($"日志文件：{Logger.CurrentLogPath}");
        var assemblyName = typeof(App).Assembly.GetName();
        Logger.Info(
            $"启动环境：Assembly={assemblyName.Name}; Version={assemblyName.Version}; " +
            $"Runtime={RuntimeInformation.FrameworkDescription}; OS={RuntimeInformation.OSDescription}; " +
            $"ProcessPath={Environment.ProcessPath ?? "未知"}");

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
    }

    /// <summary>
    /// 软件退出时调用。
    /// </summary>
    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Info($"软件退出，ExitCode={e.ApplicationExitCode}");
        base.OnExit(e);
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
