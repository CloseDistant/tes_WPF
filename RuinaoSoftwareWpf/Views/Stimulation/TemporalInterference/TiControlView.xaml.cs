using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace RuinaoSoftwareWpf.Views;

/// <summary>
/// TI 控制页面视图。
/// 这是软件最核心的操作页面：左侧 TI 刺激组列表、右侧通道参数面板。
/// 逻辑由 TiControlViewModel 和 MainViewModel 提供，这里只负责加载 XAML。
/// </summary>
public partial class TiControlView : UserControl
{
    public TiControlView()
    {
        InitializeComponent();
    }

    private void CommitFocusedInputOnBlankClick(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox
            && e.OriginalSource is DependencyObject source
            && !IsInteractiveElement(source))
        {
            Keyboard.ClearFocus();
        }
    }

    private static bool IsInteractiveElement(DependencyObject source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is TextBoxBase or ButtonBase or Selector)
            {
                return true;
            }
        }

        return false;
    }
}
