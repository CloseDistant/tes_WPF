namespace RuinaoSoftwareWpf.Views.Dialogs;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Text.RegularExpressions;

public partial class PrescriptionEditorDialog : Window
{
    private readonly PrescriptionEditorViewModel viewModel;
    private readonly IToastService toastService;
    private readonly Dictionary<TextBox, string> previousParameterValues = [];

    public PrescriptionEditorDialog(
        PrescriptionDefinition prescription,
        bool isNew,
        IEnumerable<string> availableStimulationTypes,
        IToastService toastService)
    {
        InitializeComponent();
        this.toastService = toastService;
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

    private void NextButton_Click(object sender, RoutedEventArgs e) =>
        viewModel.ContinueToEditor();

    private void Decimal_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = sender is not System.Windows.Controls.TextBox textBox
            || !Regex.IsMatch(BuildCandidate(textBox, e.Text), "^[0-9]*([.][0-9]*)?$", RegexOptions.CultureInvariant);

    private void Integer_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = sender is not System.Windows.Controls.TextBox textBox
            || !Regex.IsMatch(BuildCandidate(textBox, e.Text), "^[0-9]*$", RegexOptions.CultureInvariant);

    private void ModeNumeric_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            e.Handled = true;
            return;
        }

        var allowDecimal = viewModel.IsDirectCurrent
            || viewModel.IsPulseCurrent
                && textBox.Tag is nameof(DirectCurrentParameterKind.TotalDurationSeconds);
        e.Handled = !Regex.IsMatch(
            BuildCandidate(textBox, e.Text),
            allowDecimal ? "^[0-9]*([.][0-9]*)?$" : "^[0-9]*$",
            RegexOptions.CultureInvariant);
    }

    private void Numeric_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        var pastedText = e.DataObject.GetData(DataFormats.Text) as string ?? string.Empty;
        var allowDecimal = sender is FrameworkElement { Tag: "CurrentMilliamp" }
            || viewModel.IsDirectCurrent
            || viewModel.IsPulseCurrent
                && sender is FrameworkElement { Tag: nameof(DirectCurrentParameterKind.TotalDurationSeconds) };
        if (sender is not TextBox textBox
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

    private void RememberDirectCurrentValue(object sender, KeyboardFocusChangedEventArgs e)
    {
        if ((viewModel.IsDirectCurrent || viewModel.IsPulseCurrent)
            && sender is TextBox textBox)
        {
            previousParameterValues[textBox] = textBox.Text;
        }
    }

    private void NormalizeDirectCurrentValue(object sender, KeyboardFocusChangedEventArgs e)
    {
        if ((!viewModel.IsDirectCurrent && !viewModel.IsPulseCurrent)
            || sender is not TextBox textBox
            || textBox.Tag is not string kindName)
        {
            return;
        }

        var normalization = NormalizeEntry(kindName, textBox);
        if (normalization is null)
        {
            return;
        }

        textBox.Text = normalization.Value;
        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

        var expression = BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
        if (expression is not null)
        {
            Validation.ClearInvalid(expression);
        }
        textBox.ToolTip = null;
        previousParameterValues[textBox] = normalization.Value;
        if (!normalization.IsValid)
        {
            viewModel.ReportInputError(string.Empty);
            toastService.Show(ToastKind.Warning, "参数已调整", normalization.ErrorMessage);
        }
    }

    private ParameterNormalizationView? NormalizeEntry(string kindName, TextBox textBox)
    {
        if (viewModel.IsDirectCurrent
            && Enum.TryParse<DirectCurrentParameterKind>(kindName, out var directCurrentKind))
        {
            var fallback = previousParameterValues.GetValueOrDefault(
                textBox,
                GetDefaultValue(directCurrentKind));
            var result = viewModel.NormalizeDirectCurrentEntry(
                directCurrentKind,
                textBox.Text,
                fallback);
            return new ParameterNormalizationView(result.IsValid, result.Value, result.ErrorMessage);
        }

        if (viewModel.IsPulseCurrent
            && TryMapPulseCurrentKind(kindName, out var pulseCurrentKind))
        {
            var fallback = previousParameterValues.GetValueOrDefault(
                textBox,
                GetDefaultValue(pulseCurrentKind));
            var result = viewModel.NormalizePulseCurrentEntry(
                pulseCurrentKind,
                textBox.Text,
                fallback);
            return new ParameterNormalizationView(result.IsValid, result.Value, result.ErrorMessage);
        }

        return null;
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

    private void ClearNumericValidation(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        var expression = BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
        if (expression is not null && Validation.GetHasError(textBox))
        {
            Validation.ClearInvalid(expression);
            textBox.ToolTip = null;
        }
    }

    private static string GetDefaultValue(DirectCurrentParameterKind kind)
    {
        return kind switch
        {
            DirectCurrentParameterKind.CurrentMilliamp => DirectCurrentParameterRules.DefaultCurrentMilliamp,
            DirectCurrentParameterKind.RampUpSeconds => DirectCurrentParameterRules.DefaultRampUpSeconds,
            DirectCurrentParameterKind.RampDownSeconds => DirectCurrentParameterRules.DefaultRampDownSeconds,
            DirectCurrentParameterKind.TotalDurationSeconds => DirectCurrentParameterRules.DefaultTotalDurationSeconds,
            DirectCurrentParameterKind.IntervalSeconds => DirectCurrentParameterRules.DefaultIntervalSeconds,
            DirectCurrentParameterKind.SingleDurationSeconds => DirectCurrentParameterRules.DefaultSingleDurationSeconds,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知 tDCS 参数。")
        };
    }

    private static bool TryMapPulseCurrentKind(
        string kindName,
        out PulseCurrentParameterKind kind)
    {
        kind = kindName switch
        {
            nameof(DirectCurrentParameterKind.CurrentMilliamp) =>
                PulseCurrentParameterKind.CurrentMilliamp,
            nameof(DirectCurrentParameterKind.TotalDurationSeconds) =>
                PulseCurrentParameterKind.TreatmentDurationSeconds,
            nameof(DirectCurrentParameterKind.IntervalSeconds) =>
                PulseCurrentParameterKind.IntervalWidthMilliseconds,
            nameof(DirectCurrentParameterKind.SingleDurationSeconds) =>
                PulseCurrentParameterKind.PulseWidthMilliseconds,
            nameof(DirectCurrentParameterKind.RampUpSeconds) =>
                PulseCurrentParameterKind.RiseWidthMilliseconds,
            _ => (PulseCurrentParameterKind)(-1)
        };
        return Enum.IsDefined(kind);
    }

    private static string GetDefaultValue(PulseCurrentParameterKind kind)
    {
        return kind switch
        {
            PulseCurrentParameterKind.CurrentMilliamp => PulseCurrentParameterRules.DefaultCurrentMilliamp,
            PulseCurrentParameterKind.PulseWidthMilliseconds => PulseCurrentParameterRules.DefaultPulseWidthMilliseconds,
            PulseCurrentParameterKind.RiseWidthMilliseconds => PulseCurrentParameterRules.DefaultRiseWidthMilliseconds,
            PulseCurrentParameterKind.IntervalWidthMilliseconds => PulseCurrentParameterRules.DefaultIntervalWidthMilliseconds,
            PulseCurrentParameterKind.TreatmentDurationSeconds => PulseCurrentParameterRules.DefaultTreatmentDurationSeconds,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知 tPCS 参数。")
        };
    }

    private sealed record ParameterNormalizationView(
        bool IsValid,
        string Value,
        string ErrorMessage);
}
