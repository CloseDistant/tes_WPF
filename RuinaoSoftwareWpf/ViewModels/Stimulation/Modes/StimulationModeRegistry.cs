namespace RuinaoSoftwareWpf;

public sealed class StimulationModeRegistry
{
    private readonly IReadOnlyList<IStimulationModeModule> modules;
    private readonly IReadOnlyDictionary<string, IStimulationModeModule> modulesByCode;
    private readonly IReadOnlyDictionary<ObservableObject, IStimulationModeModule> modulesByPage;

    public StimulationModeRegistry(IEnumerable<IStimulationModeModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        var materialized = modules.ToArray();
        this.modules = materialized;
        modulesByCode = BuildCodeIndex(materialized);
        modulesByPage = BuildPageIndex(materialized);

        var missingModes = FeatureCatalog.StimulationTypes
            .Where(definition => !modulesByCode.ContainsKey(definition.ModeCode))
            .Select(definition => definition.ModeCode)
            .ToArray();
        if (missingModes.Length > 0)
        {
            throw new InvalidOperationException(
                $"Stimulation modes are visible but not registered: {string.Join(", ", missingModes)}.");
        }
    }

    public IReadOnlyList<IStimulationModeModule> Modules => modules;

    public IStimulationModeModule GetRequired(string modeCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modeCode);
        return modulesByCode.TryGetValue(modeCode, out var module)
            ? module
            : throw new ArgumentOutOfRangeException(
                nameof(modeCode),
                modeCode,
                "The stimulation mode is not registered.");
    }

    public bool TryGet(string? modeCode, out IStimulationModeModule? module)
    {
        if (string.IsNullOrWhiteSpace(modeCode))
        {
            module = null;
            return false;
        }

        return modulesByCode.TryGetValue(modeCode, out module);
    }

    public bool TryFindByPage(
        ObservableObject? pageViewModel,
        out IStimulationModeModule? module)
    {
        if (pageViewModel is null)
        {
            module = null;
            return false;
        }

        return modulesByPage.TryGetValue(pageViewModel, out module);
    }

    private static IReadOnlyDictionary<string, IStimulationModeModule> BuildCodeIndex(
        IReadOnlyCollection<IStimulationModeModule> modules)
    {
        var index = new Dictionary<string, IStimulationModeModule>(StringComparer.Ordinal);
        foreach (var module in modules)
        {
            ArgumentNullException.ThrowIfNull(module);
            var definition = module.Definition
                ?? throw new InvalidOperationException("A stimulation mode has no definition.");
            var catalogDefinition = FeatureCatalog.GetStimulationType(definition.ModeCode);
            if (!string.Equals(catalogDefinition.Key, definition.Key, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Stimulation mode {definition.ModeCode} does not match the feature catalog.");
            }

            if (!index.TryAdd(definition.ModeCode, module))
            {
                throw new InvalidOperationException(
                    $"Stimulation mode {definition.ModeCode} is registered more than once.");
            }
        }

        return index;
    }

    private static IReadOnlyDictionary<ObservableObject, IStimulationModeModule> BuildPageIndex(
        IReadOnlyCollection<IStimulationModeModule> modules)
    {
        var index = new Dictionary<ObservableObject, IStimulationModeModule>(
            ReferenceEqualityComparer.Instance);
        foreach (var module in modules)
        {
            if (!index.TryAdd(module.PageViewModel, module))
            {
                throw new InvalidOperationException(
                    $"Page {module.PageViewModel.GetType().Name} is shared by multiple stimulation modes.");
            }
        }

        return index;
    }
}
