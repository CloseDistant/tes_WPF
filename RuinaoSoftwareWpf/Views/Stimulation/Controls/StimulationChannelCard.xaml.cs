using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Text.RegularExpressions;

namespace RuinaoSoftwareWpf.Views.Controls;

public partial class StimulationChannelCard : UserControl
{
    private readonly Dictionary<TextBox, string> previousDirectCurrentValues = [];

    public static readonly DependencyProperty LocalizationProperty = DependencyProperty.Register(
        nameof(Localization),
        typeof(LocalizationViewModel),
        typeof(StimulationChannelCard));

    public static readonly DependencyProperty StartCommandProperty = DependencyProperty.Register(
        nameof(StartCommand),
        typeof(ICommand),
        typeof(StimulationChannelCard));

    public static readonly DependencyProperty StopCommandProperty = DependencyProperty.Register(
        nameof(StopCommand),
        typeof(ICommand),
        typeof(StimulationChannelCard));

    public static readonly DependencyProperty SelectCommandProperty = DependencyProperty.Register(
        nameof(SelectCommand),
        typeof(ICommand),
        typeof(StimulationChannelCard));

    public static readonly DependencyProperty UsePrescriptionCommandProperty = DependencyProperty.Register(
        nameof(UsePrescriptionCommand),
        typeof(ICommand),
        typeof(StimulationChannelCard));

    public static readonly DependencyProperty ShowCarrierFrequencyProperty = DependencyProperty.Register(
        nameof(ShowCarrierFrequency),
        typeof(bool),
        typeof(StimulationChannelCard),
        new PropertyMetadata(true));

    public static readonly DependencyProperty ShowPolarityProperty = DependencyProperty.Register(
        nameof(ShowPolarity),
        typeof(bool),
        typeof(StimulationChannelCard),
        new PropertyMetadata(true));

    public static readonly DependencyProperty IsMonophasicPulseCurrentProperty = DependencyProperty.Register(
        nameof(IsMonophasicPulseCurrent),
        typeof(bool),
        typeof(StimulationChannelCard),
        new PropertyMetadata(false));

    public static readonly DependencyProperty IsAlternatingCurrentProperty = DependencyProperty.Register(
        nameof(IsAlternatingCurrent),
        typeof(bool),
        typeof(StimulationChannelCard),
        new PropertyMetadata(false));

    public static readonly DependencyProperty AlternatingCurrentModeCodeProperty = DependencyProperty.Register(
        nameof(AlternatingCurrentModeCode),
        typeof(string),
        typeof(StimulationChannelCard),
        new PropertyMetadata(StimulationModeCodes.TemporalInterference));

    public static readonly DependencyProperty ShowRampDownProperty = DependencyProperty.Register(
        nameof(ShowRampDown),
        typeof(bool),
        typeof(StimulationChannelCard),
        new PropertyMetadata(true));

    public static readonly DependencyProperty ShowModeProperty = DependencyProperty.Register(
        nameof(ShowMode),
        typeof(bool),
        typeof(StimulationChannelCard),
        new PropertyMetadata(true));

    public static readonly DependencyProperty ShowSingleDurationProperty = DependencyProperty.Register(
        nameof(ShowSingleDuration),
        typeof(bool),
        typeof(StimulationChannelCard),
        new PropertyMetadata(true));

    public static readonly DependencyProperty ShowIntervalTimeProperty = DependencyProperty.Register(
        nameof(ShowIntervalTime),
        typeof(bool),
        typeof(StimulationChannelCard),
        new PropertyMetadata(true));

    public static readonly DependencyProperty ShowElectrodeDescriptionProperty = DependencyProperty.Register(
        nameof(ShowElectrodeDescription),
        typeof(bool),
        typeof(StimulationChannelCard),
        new PropertyMetadata(true));

    public static readonly DependencyProperty EnableSimulatedWaveformProperty = DependencyProperty.Register(
        nameof(EnableSimulatedWaveform),
        typeof(bool),
        typeof(StimulationChannelCard),
        new PropertyMetadata(false));

    public static readonly DependencyProperty HighlightBorderProperty = DependencyProperty.Register(
        nameof(HighlightBorder),
        typeof(bool),
        typeof(StimulationChannelCard),
        new PropertyMetadata(true));

    public static readonly DependencyProperty ParameterValidationFailedCommandProperty = DependencyProperty.Register(
        nameof(ParameterValidationFailedCommand),
        typeof(ICommand),
        typeof(StimulationChannelCard));

    public StimulationChannelCard()
    {
        InitializeComponent();
    }

    public LocalizationViewModel? Localization
    {
        get => (LocalizationViewModel?)GetValue(LocalizationProperty);
        set => SetValue(LocalizationProperty, value);
    }

    public ICommand? StartCommand
    {
        get => (ICommand?)GetValue(StartCommandProperty);
        set => SetValue(StartCommandProperty, value);
    }

    public ICommand? StopCommand
    {
        get => (ICommand?)GetValue(StopCommandProperty);
        set => SetValue(StopCommandProperty, value);
    }

    public ICommand? SelectCommand
    {
        get => (ICommand?)GetValue(SelectCommandProperty);
        set => SetValue(SelectCommandProperty, value);
    }

    public ICommand? UsePrescriptionCommand
    {
        get => (ICommand?)GetValue(UsePrescriptionCommandProperty);
        set => SetValue(UsePrescriptionCommandProperty, value);
    }

    public bool ShowCarrierFrequency
    {
        get => (bool)GetValue(ShowCarrierFrequencyProperty);
        set => SetValue(ShowCarrierFrequencyProperty, value);
    }

    public bool ShowPolarity
    {
        get => (bool)GetValue(ShowPolarityProperty);
        set => SetValue(ShowPolarityProperty, value);
    }

    public bool IsMonophasicPulseCurrent
    {
        get => (bool)GetValue(IsMonophasicPulseCurrentProperty);
        set => SetValue(IsMonophasicPulseCurrentProperty, value);
    }

    public bool IsAlternatingCurrent
    {
        get => (bool)GetValue(IsAlternatingCurrentProperty);
        set => SetValue(IsAlternatingCurrentProperty, value);
    }

    public string AlternatingCurrentModeCode
    {
        get => (string)GetValue(AlternatingCurrentModeCodeProperty);
        set => SetValue(AlternatingCurrentModeCodeProperty, value);
    }

    public bool ShowRampDown
    {
        get => (bool)GetValue(ShowRampDownProperty);
        set => SetValue(ShowRampDownProperty, value);
    }

    public bool ShowMode
    {
        get => (bool)GetValue(ShowModeProperty);
        set => SetValue(ShowModeProperty, value);
    }

    public bool ShowSingleDuration
    {
        get => (bool)GetValue(ShowSingleDurationProperty);
        set => SetValue(ShowSingleDurationProperty, value);
    }

    public bool ShowIntervalTime
    {
        get => (bool)GetValue(ShowIntervalTimeProperty);
        set => SetValue(ShowIntervalTimeProperty, value);
    }

    public bool ShowElectrodeDescription
    {
        get => (bool)GetValue(ShowElectrodeDescriptionProperty);
        set => SetValue(ShowElectrodeDescriptionProperty, value);
    }

    public bool EnableSimulatedWaveform
    {
        get => (bool)GetValue(EnableSimulatedWaveformProperty);
        set => SetValue(EnableSimulatedWaveformProperty, value);
    }

    public bool HighlightBorder
    {
        get => (bool)GetValue(HighlightBorderProperty);
        set => SetValue(HighlightBorderProperty, value);
    }

    public ICommand? ParameterValidationFailedCommand
    {
        get => (ICommand?)GetValue(ParameterValidationFailedCommandProperty);
        set => SetValue(ParameterValidationFailedCommandProperty, value);
    }

    private void ToggleWaveformViewMode(object sender, RoutedEventArgs e)
    {
        if (DataContext is ChannelConfig channel)
        {
            channel.DirectCurrentWaveform.ToggleViewMode();
        }
    }

    private void SelectChannelOnInteraction(object sender, RoutedEventArgs e)
    {
        if (SelectCommand?.CanExecute(DataContext) == true)
        {
            SelectCommand.Execute(DataContext);
        }
    }

    private void RememberDirectCurrentValue(object sender, KeyboardFocusChangedEventArgs e)
    {
        if ((IsDirectCurrentCard || IsMonophasicPulseCurrent || IsAlternatingCurrent)
            && sender is TextBox textBox)
        {
            previousDirectCurrentValues[textBox] = textBox.Text;
        }
    }

    private void NormalizeDirectCurrentValue(object sender, KeyboardFocusChangedEventArgs e)
    {
        if ((!IsDirectCurrentCard && !IsMonophasicPulseCurrent && !IsAlternatingCurrent)
            || sender is not TextBox textBox
            || textBox.Tag is not string kindName)
        {
            return;
        }


        if (IsAlternatingCurrent)
        {
            NormalizeAlternatingCurrentValue(textBox, kindName);
            return;
        }

        if (IsMonophasicPulseCurrent)
        {
            NormalizeMonophasicPulseCurrentValue(textBox, kindName);
            return;
        }

        if (!Enum.TryParse<DirectCurrentParameterKind>(kindName, out var kind))
        {
            return;
        }

        var fallback = previousDirectCurrentValues.GetValueOrDefault(
            textBox,
            GetDefaultValue(kind));
        var result = DirectCurrentParameterRules.Normalize(kind, textBox.Text, fallback);
        textBox.Text = result.Value;
        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

        var expression = BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
        if (expression is not null)
        {
            Validation.ClearInvalid(expression);
        }
        textBox.ToolTip = null;
        previousDirectCurrentValues[textBox] = result.Value;
        if (!result.IsValid
            && ParameterValidationFailedCommand?.CanExecute(result.ErrorMessage) == true)
        {
            ParameterValidationFailedCommand.Execute(result.ErrorMessage);
        }
    }

    private void NormalizeMonophasicPulseCurrentValue(TextBox textBox, string kindName)
    {
        var kind = kindName switch
        {
            nameof(DirectCurrentParameterKind.CurrentMilliamp) => MonophasicPulseCurrentParameterKind.CurrentMilliamp,
            nameof(DirectCurrentParameterKind.RampUpSeconds) => MonophasicPulseCurrentParameterKind.RampSeconds,
            nameof(DirectCurrentParameterKind.IntervalSeconds) => MonophasicPulseCurrentParameterKind.IntervalSeconds,
            nameof(DirectCurrentParameterKind.TotalDurationSeconds) => MonophasicPulseCurrentParameterKind.TotalDurationSeconds,
            _ => (MonophasicPulseCurrentParameterKind)(-1)
        };
        if ((int)kind < 0)
        {
            return;
        }

        var fallback = previousDirectCurrentValues.GetValueOrDefault(
            textBox,
            MonophasicPulseCurrentParameterRules.GetDefault(kind));
        var result = MonophasicPulseCurrentParameterRules.Normalize(kind, textBox.Text, fallback);
        textBox.Text = result.Value;
        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (kind == MonophasicPulseCurrentParameterKind.RampSeconds
            && DataContext is ChannelConfig channel
            && double.TryParse(result.Value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var ramp))
        {
            channel.RampDownS = result.Value;
            channel.SingleDurationS = (ramp * 2d).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }

        var expression = BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
        if (expression is not null)
        {
            Validation.ClearInvalid(expression);
        }

        textBox.ToolTip = null;
        previousDirectCurrentValues[textBox] = result.Value;
        if (!result.IsValid
            && ParameterValidationFailedCommand?.CanExecute(result.ErrorMessage) == true)
        {
            ParameterValidationFailedCommand.Execute(result.ErrorMessage);
        }
    }

    private void NormalizeAlternatingCurrentValue(TextBox textBox, string kindName)
    {
        if (string.Equals(
                AlternatingCurrentModeCode,
                StimulationModeCodes.AlternatingCurrent,
                StringComparison.Ordinal))
        {
            NormalizeTacsValue(textBox, kindName);
            return;
        }

        var kind = kindName switch
        {
            nameof(DirectCurrentParameterKind.CurrentMilliamp) =>
                TiAlternatingCurrentParameterKind.PeakCurrentMilliampere,
            nameof(DirectCurrentParameterKind.RampUpSeconds) =>
                TiAlternatingCurrentParameterKind.RampUpSeconds,
            nameof(DirectCurrentParameterKind.RampDownSeconds) =>
                TiAlternatingCurrentParameterKind.RampDownSeconds,
            nameof(DirectCurrentParameterKind.TotalDurationSeconds) =>
                TiAlternatingCurrentParameterKind.TotalDurationSeconds,
            nameof(TiAlternatingCurrentParameterKind.FrequencyHz) =>
                TiAlternatingCurrentParameterKind.FrequencyHz,
            _ => (TiAlternatingCurrentParameterKind)(-1),
        };
        if ((int)kind < 0)
        {
            return;
        }

        var fallback = previousDirectCurrentValues.GetValueOrDefault(
            textBox,
            TiAlternatingCurrentParameterRules.GetDefault(kind));
        var result = TiAlternatingCurrentParameterRules.Normalize(kind, textBox.Text, fallback);
        textBox.Text = result.Value;
        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

        var expression = BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
        if (expression is not null)
        {
            Validation.ClearInvalid(expression);
        }

        textBox.ToolTip = null;
        previousDirectCurrentValues[textBox] = result.Value;
        if (!result.IsValid
            && ParameterValidationFailedCommand?.CanExecute(result.ErrorMessage) == true)
        {
            ParameterValidationFailedCommand.Execute(result.ErrorMessage);
        }
    }

    private void NormalizeTacsValue(TextBox textBox, string kindName)
    {
        var kind = kindName switch
        {
            nameof(DirectCurrentParameterKind.CurrentMilliamp) => TacsParameterKind.PeakCurrentMilliampere,
            nameof(DirectCurrentParameterKind.RampUpSeconds) => TacsParameterKind.RampUpSeconds,
            nameof(DirectCurrentParameterKind.RampDownSeconds) => TacsParameterKind.RampDownSeconds,
            nameof(DirectCurrentParameterKind.TotalDurationSeconds) => TacsParameterKind.TotalDurationSeconds,
            nameof(TiAlternatingCurrentParameterKind.FrequencyHz) => TacsParameterKind.FrequencyHz,
            _ => (TacsParameterKind)(-1),
        };
        if ((int)kind < 0)
        {
            return;
        }

        var fallback = previousDirectCurrentValues.GetValueOrDefault(
            textBox,
            TacsParameterRules.GetDefault(kind));
        var result = TacsParameterRules.Normalize(kind, textBox.Text, fallback);
        textBox.Text = result.Value;
        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

        var expression = BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
        if (expression is not null)
        {
            Validation.ClearInvalid(expression);
        }

        textBox.ToolTip = null;
        previousDirectCurrentValues[textBox] = result.Value;
        if (!result.IsValid
            && ParameterValidationFailedCommand?.CanExecute(result.ErrorMessage) == true)
        {
            ParameterValidationFailedCommand.Execute(result.ErrorMessage);
        }
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

    private void Decimal_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = sender is not TextBox textBox || !IsDecimalCandidate(BuildCandidate(textBox, e.Text));
    }

    private void Decimal_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox textBox
            || !e.DataObject.GetDataPresent(DataFormats.Text)
            || e.DataObject.GetData(DataFormats.Text) is not string pastedText
            || !IsDecimalCandidate(BuildCandidate(textBox, pastedText)))
        {
            e.CancelCommand();
        }
    }

    private bool IsDirectCurrentCard => ShowPolarity && !ShowCarrierFrequency && !IsAlternatingCurrent;

    private static bool IsDecimalCandidate(string value) =>
        Regex.IsMatch(value, "^[0-9]*([.][0-9]*)?$", RegexOptions.CultureInvariant);

    private static string BuildCandidate(TextBox textBox, string insertedText)
    {
        var text = textBox.Text.Remove(textBox.SelectionStart, textBox.SelectionLength);
        return text.Insert(textBox.SelectionStart, insertedText);
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
}
