using System.Windows.Controls;
using System.Windows.Input;

namespace RuinaoSoftwareWpf.Views;

public partial class AssessmentPatientMatchingView : UserControl
{
    public AssessmentPatientMatchingView()
    {
        InitializeComponent();
    }

    private void PageNumberInput_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox pageInput)
        {
            return;
        }

        pageInput.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (DataContext is AssessmentPatientMatchingViewModel viewModel
            && viewModel.GoToPageCommand.CanExecute(null))
        {
            viewModel.GoToPageCommand.Execute(null);
            e.Handled = true;
        }
    }
}
