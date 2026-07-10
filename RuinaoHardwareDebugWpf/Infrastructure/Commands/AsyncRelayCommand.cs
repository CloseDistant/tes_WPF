using System.Windows.Input;

namespace RuinaoHardwareDebugWpf;

/// <summary>
/// 异步命令：用于把按钮点击和异步操作（如连接硬件、发送参数）绑定起来。
///
/// 为什么需要它？
/// WPF 的 ICommand 默认是同步的；点击“开始刺激”后可能要等几百毫秒发协议帧，
/// 如果阻塞 UI 线程，界面会卡死。AsyncRelayCommand 让命令在后台执行，并自动禁用按钮。
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    // execute：真正的异步操作，参数 + CancellationToken。
    // canExecute：判断当前能不能执行（比如正在执行时返回 false）。
    // onError：执行抛异常时的回调，通常用来写日志或弹提示。
    private readonly Func<object?, CancellationToken, Task> execute;
    private readonly Predicate<object?>? canExecute;
    private readonly Action<Exception>? onError;

    // 当前正在执行的取消令牌源，调用 Cancel() 可以中止操作。
    private CancellationTokenSource? activeCts;
    private bool isExecuting;

    /// <summary>
    /// 简化版构造函数：不需要参数时使用。
    /// </summary>
    public AsyncRelayCommand(
        Func<CancellationToken, Task> execute,
        Func<bool>? canExecute = null,
        Action<Exception>? onError = null)
        : this((_, token) => execute(token), canExecute is null ? null : _ => canExecute(), onError)
    {
    }

    /// <summary>
    /// 完整版构造函数：支持命令参数。
    /// </summary>
    public AsyncRelayCommand(
        Func<object?, CancellationToken, Task> execute,
        Predicate<object?>? canExecute = null,
        Action<Exception>? onError = null)
    {
        this.execute = execute;
        this.canExecute = canExecute;
        this.onError = onError;
    }

    // CanExecuteChanged 事件：当“能不能执行”的状态变化时触发，WPF 按钮会自动刷新可用状态。
    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// 当前是否正在执行。执行中为 true，按钮会被自动禁用。
    /// </summary>
    public bool IsExecuting
    {
        get => isExecuting;
        private set
        {
            if (isExecuting == value)
            {
                return;
            }

            isExecuting = value;
            // 状态变化时通知 WPF 重新判断按钮是否可用。
            RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// 判断命令当前能否执行。
    /// 规则：不在执行中，且 canExecute 委托没返回 false。
    /// </summary>
    public bool CanExecute(object? parameter)
    {
        return !IsExecuting && (canExecute?.Invoke(parameter) ?? true);
    }

    /// <summary>
    /// WPF 按钮被点击时调用。
    /// 注意：它是 async void，UI 触发后自动在后台执行；异常通过 onError 捕获。
    /// </summary>
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        activeCts = new CancellationTokenSource();
        IsExecuting = true;

        try
        {
            await execute(parameter, activeCts.Token);
        }
        catch (OperationCanceledException)
        {
            // 用户取消或程序关闭，属于正常流程，不需要报错。
        }
        catch (Exception ex)
        {
            // 把异常交给调用方处理，通常是写日志 + 状态栏提示。
            onError?.Invoke(ex);
        }
        finally
        {
            activeCts.Dispose();
            activeCts = null;
            IsExecuting = false;
        }
    }

    /// <summary>
    /// 取消当前正在执行的命令。
    /// </summary>
    public void Cancel() => activeCts?.Cancel();

    /// <summary>
    /// 手动触发 CanExecuteChanged，告诉 WPF 重新判断按钮状态。
    /// </summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
