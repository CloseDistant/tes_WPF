namespace RuinaoSoftwareWpf;

using System.Collections.ObjectModel;
using System.Windows.Input;

/// <summary>
/// Catalog-driven stimulation entry page. Adding a mode no longer requires a new command,
/// visibility property or shell event; the mode card is generated from FeatureCatalog.
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
        OpenModeCommand = new RelayCommand(OpenMode);

        featureVisibilityService.VisibilityChanged += (_, _) => RefreshVisibility();
        localization.PropertyChanged += (_, _) => RefreshVisibility();
        RefreshVisibility();
    }

    public LocalizationViewModel Localization { get; }

    public ObservableCollection<StimulationTypeCardViewModel> VisibleModes { get; } = [];

    public ICommand OpenModeCommand { get; }

    public event EventHandler<StimulationModeRequestedEventArgs>? ModeRequested;

    public void RefreshVisibility()
    {
        var cards = FeatureCatalog.StimulationTypes
            .Where(definition => featureVisibilityService.IsVisible(definition.Key))
            .Select(definition => new StimulationTypeCardViewModel(
                definition.ModeCode,
                Localization.FeatureText(definition.LocalizationKey),
                definition.IconGlyph))
            .ToArray();

        VisibleModes.Clear();
        foreach (var card in cards)
        {
            VisibleModes.Add(card);
        }
    }

    private void OpenMode(object? parameter)
    {
        if (parameter is StimulationTypeCardViewModel card)
        {
            ModeRequested?.Invoke(this, new StimulationModeRequestedEventArgs(card.ModeCode));
        }
    }
}
