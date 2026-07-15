namespace RuinaoHardwareDebugWpf;

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

        featureVisibilityService.VisibilityChanged += (_, _) => RefreshVisibility();
    }

    public LocalizationViewModel Localization { get; }

    public ICommand OpenTemporalInterferenceCommand { get; }

    public ICommand OpenDirectCurrentCommand { get; }

    public Visibility TemporalInterferenceVisibility => IsTemporalInterferenceVisible
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility DirectCurrentVisibility => IsDirectCurrentVisible
        ? Visibility.Visible
        : Visibility.Collapsed;

    public GridLength TemporalInterferenceColumnWidth => IsTemporalInterferenceVisible
        ? new GridLength(1, GridUnitType.Star)
        : new GridLength(0);

    public GridLength CardSpacingColumnWidth => IsTemporalInterferenceVisible && IsDirectCurrentVisible
        ? new GridLength(22)
        : new GridLength(0);

    public GridLength DirectCurrentColumnWidth => IsDirectCurrentVisible
        ? new GridLength(1, GridUnitType.Star)
        : new GridLength(0);

    public event EventHandler? TemporalInterferenceRequested;

    public event EventHandler? DirectCurrentRequested;

    public void RefreshVisibility()
    {
        OnPropertyChanged(nameof(TemporalInterferenceVisibility));
        OnPropertyChanged(nameof(DirectCurrentVisibility));
        OnPropertyChanged(nameof(TemporalInterferenceColumnWidth));
        OnPropertyChanged(nameof(CardSpacingColumnWidth));
        OnPropertyChanged(nameof(DirectCurrentColumnWidth));
    }

    private bool IsTemporalInterferenceVisible =>
        featureVisibilityService.IsVisible(FeatureKeys.StimulationTemporalInterference);

    private bool IsDirectCurrentVisible =>
        featureVisibilityService.IsVisible(FeatureKeys.StimulationDirectCurrent);
}
