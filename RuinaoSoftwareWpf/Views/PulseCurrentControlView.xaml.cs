namespace RuinaoSoftwareWpf.Views;

using System.Windows;
using System.Windows.Controls;

public partial class PulseCurrentControlView : UserControl
{
    public PulseCurrentControlView()
    {
        InitializeComponent();
    }

    private void ChannelCard_SelectOnInteraction(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PulseCurrentChannelConfig channel }
            || DataContext is not PulseCurrentControlViewModel viewModel
            || !viewModel.SelectChannelCommand.CanExecute(channel))
        {
            return;
        }

        viewModel.SelectChannelCommand.Execute(channel);
    }
}
