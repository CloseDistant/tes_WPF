namespace RuinaoSoftwareWpf.Views.Dialogs;

using System.Windows;
using System.Windows.Input;

/// <summary>tDCS 同步开始的首层操作确认弹窗。</summary>
public partial class DirectCurrentSynchronizedStartConfirmationDialog : Window
{
    public DirectCurrentSynchronizedStartConfirmationDialog(
        DirectCurrentSynchronizedStartConfirmationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        InitializeComponent();
        ScopeText.Text = $"系统将检查全部 {request.TotalChannelCount} 个通道，"
            + "并启动所有参数合法且阻抗允许的通道。";
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
