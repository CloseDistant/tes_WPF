using Xunit;

namespace RuinaoSoftwareWpf.Tests;

public sealed class StimulationTypeSelectionViewModelTests
{
    [Fact]
    public void Constructor_BuildsCardsFromFeatureCatalogOrder()
    {
        var viewModel = CreateViewModel(new TestFeatureVisibilityService());

        Assert.Equal(
            FeatureCatalog.StimulationTypes.Select(item => item.ModeCode),
            viewModel.VisibleModes.Select(item => item.ModeCode));
        Assert.Contains(
            viewModel.VisibleModes,
            item => item.ModeCode == StimulationModeCodes.PulseCurrent);
    }

    [Fact]
    public void VisibilityChange_RebuildsCardsWithoutModeSpecificProperties()
    {
        var visibility = new TestFeatureVisibilityService();
        var viewModel = CreateViewModel(visibility);

        visibility.SetVisible(FeatureKeys.StimulationDirectCurrent, false);

        Assert.DoesNotContain(
            viewModel.VisibleModes,
            item => item.ModeCode == StimulationModeCodes.DirectCurrent);
        Assert.Equal(FeatureCatalog.StimulationTypes.Count - 1, viewModel.VisibleModes.Count);
        Assert.Contains(
            viewModel.VisibleModes,
            item => item.ModeCode == StimulationModeCodes.MonophasicPulseCurrent);
    }

    [Fact]
    public void OpenModeCommand_RaisesOneGenericModeRequest()
    {
        var viewModel = CreateViewModel(new TestFeatureVisibilityService());
        string? requestedMode = null;
        viewModel.ModeRequested += (_, eventArgs) => requestedMode = eventArgs.ModeCode;
        var pulseCard = viewModel.VisibleModes.Single(
            item => item.ModeCode == StimulationModeCodes.PulseCurrent);

        viewModel.OpenModeCommand.Execute(pulseCard);

        Assert.Equal(StimulationModeCodes.PulseCurrent, requestedMode);
    }

    private static StimulationTypeSelectionViewModel CreateViewModel(
        IFeatureVisibilityService visibility)
    {
        return new StimulationTypeSelectionViewModel(
            new LocalizationViewModel(new TestLocalizationService()),
            visibility);
    }

    private sealed class TestFeatureVisibilityService : IFeatureVisibilityService
    {
        private readonly Dictionary<string, bool> visibility = FeatureCatalog.AllKeys
            .ToDictionary(key => key, _ => true, StringComparer.Ordinal);

        public event EventHandler? VisibilityChanged;

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public bool IsVisible(string featureKey) => visibility.GetValueOrDefault(featureKey);

        public Task SaveAsync(
            IReadOnlyDictionary<string, bool> visibility,
            CancellationToken cancellationToken = default)
        {
            foreach (var entry in visibility)
            {
                this.visibility[entry.Key] = entry.Value;
            }

            VisibilityChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public void SetVisible(string key, bool isVisible)
        {
            visibility[key] = isVisible;
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class TestLocalizationService : ILocalizationService
    {
        public bool IsChinese => true;

        public event EventHandler? LanguageChanged
        {
            add { }
            remove { }
        }

        public string Text(string key) => key;

        public void ToggleLanguage()
        {
        }
    }
}
