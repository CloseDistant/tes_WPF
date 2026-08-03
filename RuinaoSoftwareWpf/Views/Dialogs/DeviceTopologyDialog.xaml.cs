using System.Windows;
using System.Windows.Input;

namespace RuinaoSoftwareWpf.Views.Dialogs;

public partial class DeviceTopologyDialog : Window
{
    public DeviceTopologyDialog(DeviceTopologyDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
