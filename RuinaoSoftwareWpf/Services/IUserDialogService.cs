namespace RuinaoSoftwareWpf;

/// <summary>
/// 界面确认弹窗服务。
/// ViewModel 只关心“用户是否确认”，不直接依赖具体窗口实现，方便后续统一调整弹窗样式。
/// </summary>
public interface IUserDialogService
{
    bool ConfirmWarning(string title, string message, string confirmText, string cancelText);

    void ShowInformation(string title, string message);

    void ShowError(string title, string message);
}
