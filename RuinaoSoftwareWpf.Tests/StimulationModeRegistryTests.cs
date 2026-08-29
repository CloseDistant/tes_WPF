using Xunit;

namespace RuinaoSoftwareWpf.Tests;

public sealed class StimulationModeRegistryTests
{
    [Fact]
    public void Constructor_RegistersCatalogModesAsParallelModules()
    {
        var modules = CreateCatalogModules();

        var registry = new StimulationModeRegistry(modules);

        Assert.Equal(FeatureCatalog.StimulationTypes.Count, registry.Modules.Count);
        foreach (var module in modules)
        {
            Assert.Same(module, registry.GetRequired(module.Definition.ModeCode));
            Assert.True(registry.TryFindByPage(module.PageViewModel, out var pageModule));
            Assert.Same(module, pageModule);
        }
    }

    [Fact]
    public void Constructor_RejectsVisibleCatalogModeWithoutModule()
    {
        var modules = CreateCatalogModules()
            .Where(module => module.Definition.ModeCode != StimulationModeCodes.PulseCurrent);

        var exception = Assert.Throws<InvalidOperationException>(
            () => new StimulationModeRegistry(modules));

        Assert.Contains(StimulationModeCodes.PulseCurrent, exception.Message);
    }

    [Fact]
    public void Constructor_RejectsDuplicateModeCode()
    {
        var modules = CreateCatalogModules().ToList();
        modules.Add(new TestStimulationModeModule(StimulationModeCodes.DirectCurrent));

        var exception = Assert.Throws<InvalidOperationException>(
            () => new StimulationModeRegistry(modules));

        Assert.Contains("registered more than once", exception.Message);
    }

    [Fact]
    public void Catalog_UsesUniqueStableModeCodes()
    {
        var definitions = FeatureCatalog.StimulationTypes;

        Assert.Equal(
            definitions.Count,
            definitions.Select(item => item.ModeCode).Distinct(StringComparer.Ordinal).Count());
        Assert.All(definitions, definition =>
        {
            Assert.False(string.IsNullOrWhiteSpace(definition.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(definition.FooterStatus));
        });
        Assert.Equal(
            StimulationModeExecutionAvailability.Hardware,
            FeatureCatalog.GetStimulationType(StimulationModeCodes.PulseCurrent)
                .ExecutionAvailability);
    }

    private static TestStimulationModeModule[] CreateCatalogModules()
    {
        return FeatureCatalog.StimulationTypes
            .Select(definition => new TestStimulationModeModule(definition.ModeCode))
            .ToArray();
    }

    private sealed class TestStimulationModeModule(string modeCode) : IStimulationModeModule
    {
        public StimulationTypeFeatureDefinition Definition { get; } =
            FeatureCatalog.GetStimulationType(modeCode);

        public ObservableObject PageViewModel { get; } = new TestPageViewModel();

        public event EventHandler? BackRequested
        {
            add { }
            remove { }
        }

        public event EventHandler<HardwareOperationResult>? HardwareOperationCompleted
        {
            add { }
            remove { }
        }

        public event EventHandler<StimulationPrescriptionRequestEventArgs>? PrescriptionRequested
        {
            add { }
            remove { }
        }

        public void PrepareForActivation()
        {
        }

        public void ApplyImpedanceSnapshot(
            IReadOnlyDictionary<int, decimal?> channelImpedanceOhms)
        {
        }

        public string GetTargetChannelName(object? targetChannel) => string.Empty;

        public bool TryApplyPrescription(
            PrescriptionDefinition prescription,
            StimulationPrescriptionApplyScope scope,
            object? targetChannel,
            out string error)
        {
            error = string.Empty;
            return true;
        }
    }

    private sealed class TestPageViewModel : ObservableObject;
}
