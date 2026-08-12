namespace RuinaoSoftwareWpf.Views.Dialogs;

using System.Windows;
using System.Windows.Input;

/// <summary>
/// 采集工作台风格的确认弹窗。
/// 用于替代系统 MessageBox，保持深色界面、强调色和按钮尺寸的一致性。
/// </summary>
public partial class WorkbenchConfirmDialog : Window
{
    public WorkbenchConfirmDialog(string title, string message, string confirmText, string cancelText)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.Content = cancelText;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void DialogRoot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
