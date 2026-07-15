namespace RuinaoHardwareDebugWpf;

using System.Windows;
using RuinaoHardwareDebugWpf.Views.Dialogs;

/// <summary>
/// WPF 弹窗服务实现。
/// 当前用于采集工作台的危险操作确认，后续其他模块也可以复用。
/// </summary>
public sealed class UserDialogService : IUserDialogService
{
    public bool ConfirmWarning(string title, string message, string confirmText, string cancelText)
    {
        var dialog = new WorkbenchConfirmDialog(title, message, confirmText, cancelText)
        {
            Owner = Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true;
    }

    public void ShowInformation(string title, string message)
    {
        ShowMessageDialog(title, message, ThemedMessageKind.Information);
    }

    public void ShowError(string title, string message)
    {
        ShowMessageDialog(title, message, ThemedMessageKind.Error);
    }

    private static void ShowMessageDialog(string title, string message, ThemedMessageKind kind)
    {
        var dialog = new ThemedMessageDialog(title, message, kind)
        {
            Owner = Application.Current?.MainWindow
        };
        dialog.ShowDialog();
    }
}
