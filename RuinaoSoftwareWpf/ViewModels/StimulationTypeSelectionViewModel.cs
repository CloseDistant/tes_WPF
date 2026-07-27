namespace RuinaoSoftwareWpf;

using System.Windows;
using System.Windows.Input;

/// <summary>
/// 电刺激入口页，只负责展示当前启用的刺激类型并转发选择事件。
/// </summary>
public sealed class StimulationTypeSelectionViewModel : ObservableObject
{
    private readonly IFeatureVisibilityService featureVisibilityService;

    public StimulationTypeSelectionViewModel(
        LocalizationViewModel localization,
        IFeatureVisibilityService featureVisibilityService)
    {
        Localization = localization;
        this.featureVisibilityService = featureVisibilityService;

        OpenTemporalInterferenceCommand = new RelayCommand(
            _ => TemporalInterferenceRequested?.Invoke(this, EventArgs.Empty));
        OpenDirectCurrentCommand = new RelayCommand(
            _ => DirectCurrentRequested?.Invoke(this, EventArgs.Empty));
        OpenPulseCurrentCommand = new RelayCommand(
            _ => PulseCurrentRequested?.Invoke(this, EventArgs.Empty));

        featureVisibilityService.VisibilityChanged += (_, _) => RefreshVisibility();
    }

    public LocalizationViewModel Localization { get; }

    public ICommand OpenTemporalInterferenceCommand { get; }

    public ICommand OpenDirectCurrentCommand { get; }

    public ICommand OpenPulseCurrentCommand { get; }

    public Visibility TemporalInterferenceVisibility => IsTemporalInterferenceVisible
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility DirectCurrentVisibility => IsDirectCurrentVisible
        ? Visibility.Visible
        : Visibility.Collapsed;

    public int DirectCurrentCardColumn => IsTemporalInterferenceVisible ? 2 : 0;

    public int DirectCurrentCardRow => 0;

    public int PulseCurrentCardColumn
    {
        get
        {
            var precedingVisibleCount = (IsTemporalInterferenceVisible ? 1 : 0)
                + (IsDirectCurrentVisible ? 1 : 0);
            return precedingVisibleCount % 2 == 0 ? 0 : 2;
        }
    }

    public int PulseCurrentCardRow
    {
        get
        {
            var precedingVisibleCount = (IsTemporalInterferenceVisible ? 1 : 0)
                + (IsDirectCurrentVisible ? 1 : 0);
            return precedingVisibleCount / 2 * 2;
        }
    }

    public Visibility PulseCurrentVisibility => IsPulseCurrentVisible
        ? Visibility.Visible
        : Visibility.Collapsed;

    public event EventHandler? TemporalInterferenceRequested;

    public event EventHandler? DirectCurrentRequested;

    public event EventHandler? PulseCurrentRequested;

    public void RefreshVisibility()
    {
        OnPropertyChanged(nameof(TemporalInterferenceVisibility));
        OnPropertyChanged(nameof(DirectCurrentVisibility));
        OnPropertyChanged(nameof(DirectCurrentCardColumn));
        OnPropertyChanged(nameof(DirectCurrentCardRow));
        OnPropertyChanged(nameof(PulseCurrentVisibility));
        OnPropertyChanged(nameof(PulseCurrentCardColumn));
        OnPropertyChanged(nameof(PulseCurrentCardRow));
    }

    private bool IsTemporalInterferenceVisible =>
        featureVisibilityService.IsVisible(FeatureKeys.StimulationTemporalInterference);

    private bool IsDirectCurrentVisible =>
        featureVisibilityService.IsVisible(FeatureKeys.StimulationDirectCurrent);

    private bool IsPulseCurrentVisible =>
        featureVisibilityService.IsVisible(FeatureKeys.StimulationPulseCurrent);
}
