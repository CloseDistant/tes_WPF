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
    public const string StimulationPulseCurrent = "stimulation.tpcs";
}

/// <summary>
/// Stable stimulation mode identifiers shared by navigation, prescriptions and mode modules.
/// These values are persisted, so changing one requires an explicit data migration.
/// </summary>
public static class StimulationModeCodes
{
    public const string TemporalInterference = "TI";
    public const string DirectCurrent = "tDCS";
    public const string PulseCurrent = "tPCS";
}

public sealed record NavigationFeatureDefinition(
    string Key,
    AppPage Page,
    string LocalizationKey,
    bool DefaultVisible = true);

public enum StimulationModeExecutionAvailability
{
    Hardware,
    HardwareIntegrationPending
}

public sealed record StimulationTypeFeatureDefinition(
    string Key,
    string LocalizationKey,
    string ModeCode,
    string DisplayName,
    string IconGlyph,
    string FooterStatus,
    bool RequiresImpedanceMonitoring = false,
    StimulationModeExecutionAvailability ExecutionAvailability =
        StimulationModeExecutionAvailability.Hardware,
    bool DefaultVisible = true)
{
    // Compatibility alias for existing display bindings. New routing code must use ModeCode.
    public string ShortName => ModeCode;
}

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
        new(
            FeatureKeys.StimulationTemporalInterference,
            "TemporalInterference",
            StimulationModeCodes.TemporalInterference,
            "时间相干电刺激",
            "≈",
            "时间相干电刺激参数设置"),
        new(
            FeatureKeys.StimulationDirectCurrent,
            "TranscranialDirectCurrent",
            StimulationModeCodes.DirectCurrent,
            "经颅直流电刺激",
            "━",
            "经颅直流电刺激参数设置",
            RequiresImpedanceMonitoring: true),
        new(
            FeatureKeys.StimulationPulseCurrent,
            "TranscranialPulseCurrent",
            StimulationModeCodes.PulseCurrent,
            "经颅脉冲电流刺激",
            "⌁",
            "经颅脉冲电流刺激参数设置",
            RequiresImpedanceMonitoring: true,
            ExecutionAvailability: StimulationModeExecutionAvailability.HardwareIntegrationPending)
    ];

    public static StimulationTypeFeatureDefinition GetStimulationType(string modeCode)
    {
        return StimulationTypes.FirstOrDefault(
                item => string.Equals(item.ModeCode, modeCode, StringComparison.Ordinal))
            ?? throw new ArgumentOutOfRangeException(
                nameof(modeCode),
                modeCode,
                "Unknown stimulation mode code.");
    }

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
