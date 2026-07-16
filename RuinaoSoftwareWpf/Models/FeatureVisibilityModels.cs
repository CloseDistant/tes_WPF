namespace RuinaoSoftwareWpf;

public static class FeatureKeys
{
    public const string NavigationDashboard = "navigation.dashboard";
    public const string NavigationStimulation = "navigation.stimulation";
    public const string NavigationEeg = "navigation.eeg";
    public const string NavigationAssessment = "navigation.assessment";
    public const string NavigationClosedLoop = "navigation.closed_loop";
    public const string NavigationHeadModel = "navigation.head_model";
    public const string NavigationFem = "navigation.fem";
    public const string NavigationPrescription = "navigation.prescription";
    public const string NavigationRecords = "navigation.records";

    public const string StimulationTemporalInterference = "stimulation.ti";
    public const string StimulationDirectCurrent = "stimulation.tdcs";
}

public sealed record NavigationFeatureDefinition(
    string Key,
    AppPage Page,
    string LocalizationKey,
    bool DefaultVisible = true);

public sealed record StimulationTypeFeatureDefinition(
    string Key,
    string LocalizationKey,
    string ShortName,
    bool DefaultVisible = true);

public static class FeatureCatalog
{
    public static IReadOnlyList<NavigationFeatureDefinition> Navigation { get; } =
    [
        new(FeatureKeys.NavigationDashboard, AppPage.Dashboard, "Dashboard"),
        new(FeatureKeys.NavigationStimulation, AppPage.Control, "Control"),
        new(FeatureKeys.NavigationEeg, AppPage.EegSignalCapture, "EegSignalCapture"),
        new(FeatureKeys.NavigationAssessment, AppPage.AssessmentCapture, "AssessmentCapture"),
        new(FeatureKeys.NavigationClosedLoop, AppPage.ClosedLoopControl, "ClosedLoopControl"),
        new(FeatureKeys.NavigationHeadModel, AppPage.HeadModel, "HeadModel"),
        new(FeatureKeys.NavigationFem, AppPage.FemSimulation, "FemSimulation"),
        new(FeatureKeys.NavigationPrescription, AppPage.ProtocolManager, "ProtocolManager"),
        new(FeatureKeys.NavigationRecords, AppPage.TreatmentHistory, "TreatmentHistory")
    ];

    public static IReadOnlyList<StimulationTypeFeatureDefinition> StimulationTypes { get; } =
    [
        new(FeatureKeys.StimulationTemporalInterference, "TemporalInterference", "TI"),
        new(FeatureKeys.StimulationDirectCurrent, "TranscranialDirectCurrent", "tDCS")
    ];

    public static IReadOnlySet<string> AllKeys { get; } = Navigation
        .Select(item => item.Key)
        .Concat(StimulationTypes.Select(item => item.Key))
        .ToHashSet(StringComparer.Ordinal);

    public static bool DefaultVisibility(string key)
    {
        return Navigation.FirstOrDefault(item => item.Key == key)?.DefaultVisible
            ?? StimulationTypes.FirstOrDefault(item => item.Key == key)?.DefaultVisible
            ?? throw new ArgumentOutOfRangeException(nameof(key), key, "未知功能 Key");
    }
}
