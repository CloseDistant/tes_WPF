using System.Windows.Input;

namespace RuinaoHardwareDebugWpf;

/// <summary>
/// 同步命令：用于把按钮点击和同步操作（如切换语言、切换页面）绑定起来。
/// 特点是“立即执行、不会耗时”，适合轻量 UI 交互。
/// </summary>
public sealed class RelayCommand : ICommand
{
    // execute：点击按钮时要执行的同步操作。
    // canExecute：判断当前能否点击（可选）。
    private readonly Action<object?> execute;
    private readonly Predicate<object?>? canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        this.execute = execute;
        this.canExecute = canExecute;
    }

    // 当命令可用状态变化时触发，WPF 会自动更新按钮是否变灰。
    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// 判断按钮当前能不能点。
    /// </summary>
    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

    /// <summary>
    /// 按钮被点击时真正执行的操作。
    /// </summary>
    public void Execute(object? parameter) => execute(parameter);

    /// <summary>
    /// 手动通知 WPF 重新判断 CanExecute。
    /// </summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
