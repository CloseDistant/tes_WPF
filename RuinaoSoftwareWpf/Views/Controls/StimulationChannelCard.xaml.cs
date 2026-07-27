using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RuinaoSoftwareWpf.Views.Controls;

public partial class StimulationChannelCard : UserControl
{
    public static readonly DependencyProperty LocalizationProperty = DependencyProperty.Register(
        nameof(Localization),
        typeof(LocalizationViewModel),
        typeof(StimulationChannelCard));

    public static readonly DependencyProperty StartCommandProperty = DependencyProperty.Register(
        nameof(StartCommand),
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

    public static readonly DependencyProperty ShowElectrodeDescriptionProperty = DependencyProperty.Register(
        nameof(ShowElectrodeDescription),
        typeof(bool),
        typeof(StimulationChannelCard),
        new PropertyMetadata(true));

    public static readonly DependencyProperty ShowStatusMonitorProperty = DependencyProperty.Register(
        nameof(ShowStatusMonitor),
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

    public bool ShowElectrodeDescription
    {
        get => (bool)GetValue(ShowElectrodeDescriptionProperty);
        set => SetValue(ShowElectrodeDescriptionProperty, value);
    }

    public bool ShowStatusMonitor
    {
        get => (bool)GetValue(ShowStatusMonitorProperty);
        set => SetValue(ShowStatusMonitorProperty, value);
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
}
