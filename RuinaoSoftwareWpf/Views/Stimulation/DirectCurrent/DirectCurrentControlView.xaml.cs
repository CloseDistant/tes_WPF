namespace RuinaoSoftwareWpf.Views;

using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

public partial class DirectCurrentControlView : UserControl
{
    public DirectCurrentControlView()
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
