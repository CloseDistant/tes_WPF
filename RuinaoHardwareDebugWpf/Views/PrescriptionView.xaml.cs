namespace RuinaoHardwareDebugWpf.Views;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

public partial class PrescriptionView : UserControl
{
    public PrescriptionView() => InitializeComponent();

    private async void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PrescriptionViewModel viewModel) await viewModel.InitializeAsync();
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu, DataContext: PrescriptionDefinition prescription }
            || DataContext is not PrescriptionViewModel viewModel)
        {
            return;
        }

        viewModel.SelectedPrescription = prescription;
        menu.DataContext = prescription;
        foreach (var deleteItem in menu.Items.OfType<MenuItem>().Where(item => Equals(item.Tag, "delete")))
        {
            deleteItem.Visibility = viewModel.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        }

        menu.PlacementTarget = (Button)sender;
        menu.Placement = PlacementMode.Bottom;
        menu.HorizontalOffset = -80;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void EditMenuItem_Click(object sender, RoutedEventArgs e) => ExecuteMenuCommand(sender, vm => vm.EditCommand);

    private void CopyMenuItem_Click(object sender, RoutedEventArgs e) => ExecuteMenuCommand(sender, vm => vm.CopyCommand);

    private void DeleteMenuItem_Click(object sender, RoutedEventArgs e) => ExecuteMenuCommand(sender, vm => vm.DeleteCommand);

    private void ExecuteMenuCommand(object sender, Func<PrescriptionViewModel, System.Windows.Input.ICommand> commandSelector)
    {
        if (DataContext is not PrescriptionViewModel viewModel
            || sender is not MenuItem { CommandParameter: PrescriptionDefinition prescription })
        {
            return;
        }

        var command = commandSelector(viewModel);
        if (command.CanExecute(prescription)) command.Execute(prescription);
    }
}
