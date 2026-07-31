namespace RuinaoSoftwareWpf.Views;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Text.RegularExpressions;

public partial class PulseCurrentControlView : UserControl
{
    private readonly Dictionary<TextBox, string> previousParameterValues = [];

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

    private void Decimal_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = sender is not TextBox textBox
            || !Regex.IsMatch(
                BuildCandidate(textBox, e.Text),
                "^[0-9]*([.][0-9]*)?$",
                RegexOptions.CultureInvariant);

    private void Integer_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = sender is not TextBox textBox
            || !Regex.IsMatch(
                BuildCandidate(textBox, e.Text),
                "^[0-9]*$",
                RegexOptions.CultureInvariant);

    private void Numeric_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text)
            || sender is not TextBox textBox)
        {
            e.CancelCommand();
            return;
        }

        var pastedText = e.DataObject.GetData(DataFormats.Text) as string ?? string.Empty;
        var allowDecimal = textBox.Tag is nameof(PulseCurrentParameterKind.CurrentMilliamp)
            or nameof(PulseCurrentParameterKind.TreatmentDurationSeconds);
        if (!Regex.IsMatch(
                BuildCandidate(textBox, pastedText),
                allowDecimal ? "^[0-9]*([.][0-9]*)?$" : "^[0-9]*$",
                RegexOptions.CultureInvariant))
        {
            e.CancelCommand();
        }
    }

    private static string BuildCandidate(TextBox textBox, string insertedText)
    {
        var text = textBox.Text.Remove(textBox.SelectionStart, textBox.SelectionLength);
        return text.Insert(textBox.SelectionStart, insertedText);
    }

    private void RememberParameterValue(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            previousParameterValues[textBox] = textBox.Text;
        }
    }

    private void NormalizeParameterValue(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox
            {
                Tag: string kindName,
                DataContext: PulseCurrentChannelConfig channel
            } textBox
            || !Enum.TryParse<PulseCurrentParameterKind>(kindName, out var kind)
            || DataContext is not PulseCurrentControlViewModel viewModel)
        {
            return;
        }

        var fallback = previousParameterValues.GetValueOrDefault(
            textBox,
            GetDefaultValue(kind));
        var result = PulseCurrentParameterRules.Normalize(kind, textBox.Text, fallback);
        textBox.Text = result.Value;
        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        previousParameterValues[textBox] = result.Value;

        if (!result.IsValid
            && viewModel.ParameterValidationFailedCommand.CanExecute(result.ErrorMessage))
        {
            viewModel.ParameterValidationFailedCommand.Execute(result.ErrorMessage);
        }

        if (viewModel.RefreshPlannedTotalCountCommand.CanExecute(channel))
        {
            viewModel.RefreshPlannedTotalCountCommand.Execute(channel);
        }
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
            if (current is TextBoxBase or ButtonBase or System.Windows.Controls.Primitives.Selector)
            {
                return true;
            }
        }

        return false;
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
}
