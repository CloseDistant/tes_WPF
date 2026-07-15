using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RuinaoHardwareDebugWpf.Views.Controls;

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

    public static readonly DependencyProperty ShowCarrierFrequencyProperty = DependencyProperty.Register(
        nameof(ShowCarrierFrequency),
        typeof(bool),
        typeof(StimulationChannelCard),
        new PropertyMetadata(true));

    public static readonly DependencyProperty ShowElectrodeDescriptionProperty = DependencyProperty.Register(
        nameof(ShowElectrodeDescription),
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

    public bool ShowCarrierFrequency
    {
        get => (bool)GetValue(ShowCarrierFrequencyProperty);
        set => SetValue(ShowCarrierFrequencyProperty, value);
    }

    public bool ShowElectrodeDescription
    {
        get => (bool)GetValue(ShowElectrodeDescriptionProperty);
        set => SetValue(ShowElectrodeDescriptionProperty, value);
    }
}
