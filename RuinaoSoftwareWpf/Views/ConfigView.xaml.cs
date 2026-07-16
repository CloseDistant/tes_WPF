using System.Windows.Controls;

namespace RuinaoSoftwareWpf.Views;

/// <summary>
/// 关于页面视图（同时承载管理员功能显示配置）。
/// XAML 负责布局和样式，这个 Code-Behind 文件只调用 InitializeComponent，逻辑交给 ConfigViewModel。
/// </summary>
public partial class ConfigView : UserControl
{
    private System.Windows.Window? ownerWindow;

    public ConfigView()
    {
        InitializeComponent();
    }

    private void ConfigView_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        DetachWindowKeyHandler();
        ownerWindow = System.Windows.Window.GetWindow(this);
        if (ownerWindow is not null)
        {
            ownerWindow.PreviewKeyDown += ConfigView_PreviewKeyDown;
        }

        Focus();
        System.Windows.Input.Keyboard.Focus(this);
    }

    private void ConfigView_Unloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        DetachWindowKeyHandler();
    }

    private void ConfigView_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (DataContext is not ConfigViewModel viewModel || e.IsRepeat)
        {
            return;
        }

        if (e.Key is System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift)
        {
            viewModel.RegisterShiftPress(DateTimeOffset.Now);
            return;
        }

        viewModel.ResetShiftSequence();
    }

    private void DetachWindowKeyHandler()
    {
        if (ownerWindow is null)
        {
            return;
        }

        ownerWindow.PreviewKeyDown -= ConfigView_PreviewKeyDown;
        ownerWindow = null;
    }
}
