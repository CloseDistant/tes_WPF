using System.Windows.Controls;
using System.Windows.Input;

namespace RuinaoSoftwareWpf.Views;

public partial class MonophasicPulseCurrentControlView : UserControl
{
    public MonophasicPulseCurrentControlView()
    {
        InitializeComponent();
    }

    private void CommitFocusedInputOnBlankClick(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox textBox
            && !ReferenceEquals(e.OriginalSource, textBox))
        {
            Keyboard.ClearFocus();
        }
    }
}
