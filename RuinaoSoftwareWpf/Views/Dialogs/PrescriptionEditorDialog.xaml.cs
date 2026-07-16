namespace RuinaoSoftwareWpf.Views.Dialogs;

using System.Windows;
using System.Windows.Input;
using System.Text.RegularExpressions;

public partial class PrescriptionEditorDialog : Window
{
    private readonly PrescriptionEditorViewModel viewModel;

    public PrescriptionEditorDialog(
        PrescriptionDefinition prescription,
        bool isNew,
        IEnumerable<string> availableStimulationTypes)
    {
        InitializeComponent();
        viewModel = new PrescriptionEditorViewModel(prescription, isNew, availableStimulationTypes);
        DataContext = viewModel;
    }

    public PrescriptionDefinition? Result { get; private set; }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!viewModel.TryBuild(out var prescription)) return;
        Result = prescription;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Decimal_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = sender is not System.Windows.Controls.TextBox textBox
            || !Regex.IsMatch(BuildCandidate(textBox, e.Text), "^[0-9]*([.][0-9]*)?$", RegexOptions.CultureInvariant);

    private void Integer_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = sender is not System.Windows.Controls.TextBox textBox
            || !Regex.IsMatch(BuildCandidate(textBox, e.Text), "^[0-9]*$", RegexOptions.CultureInvariant);

    private void Numeric_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        var pastedText = e.DataObject.GetData(DataFormats.Text) as string ?? string.Empty;
        var allowDecimal = sender is FrameworkElement { Tag: "decimal" };
        if (sender is not System.Windows.Controls.TextBox textBox
            || !Regex.IsMatch(
                BuildCandidate(textBox, pastedText),
                allowDecimal ? "^[0-9]*([.][0-9]*)?$" : "^[0-9]*$",
                RegexOptions.CultureInvariant))
        {
            e.CancelCommand();
        }
    }

    private static string BuildCandidate(System.Windows.Controls.TextBox textBox, string insertedText)
    {
        var text = textBox.Text.Remove(textBox.SelectionStart, textBox.SelectionLength);
        return text.Insert(textBox.SelectionStart, insertedText);
    }
}
