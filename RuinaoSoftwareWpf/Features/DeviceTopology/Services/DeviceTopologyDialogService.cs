using System.Windows;
using RuinaoSoftwareWpf.Views.Dialogs;

namespace RuinaoSoftwareWpf;

public sealed class DeviceTopologyDialogService : IDeviceTopologyDialogService
{
    private readonly DeviceTopologyDialogViewModel viewModel;

    public DeviceTopologyDialogService(DeviceTopologyDialogViewModel viewModel)
    {
        this.viewModel = viewModel;
    }

    public void Show()
    {
        viewModel.LoadCurrentSnapshot();
        var dialog = new DeviceTopologyDialog(viewModel)
        {
            Owner = Application.Current?.MainWindow,
        };
        dialog.ShowDialog();
    }
}
