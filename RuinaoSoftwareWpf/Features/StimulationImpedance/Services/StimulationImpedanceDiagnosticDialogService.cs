using System.Windows;
using RuinaoSoftwareWpf.Views.Dialogs;

namespace RuinaoSoftwareWpf;

public sealed class StimulationImpedanceDiagnosticDialogService
    : IStimulationImpedanceDiagnosticDialogService
{
    private readonly StimulationImpedanceDiagnosticDialogViewModel viewModel;

    public StimulationImpedanceDiagnosticDialogService(
        StimulationImpedanceDiagnosticDialogViewModel viewModel)
    {
        this.viewModel = viewModel;
    }

    public void Show()
    {
        viewModel.LoadCurrentSnapshot();
        var dialog = new StimulationImpedanceDiagnosticDialog(viewModel)
        {
            Owner = Application.Current?.MainWindow,
        };
        dialog.ShowDialog();
    }
}
