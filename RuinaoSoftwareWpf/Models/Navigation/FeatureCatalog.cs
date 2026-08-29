namespace RuinaoSoftwareWpf;

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
        new(FeatureKeys.StimulationTemporalInterference, "TemporalInterference", StimulationModeCodes.TemporalInterference, "时间相干电刺激", "≈", "时间相干电刺激参数设置"),
        new(FeatureKeys.StimulationDirectCurrent, "TranscranialDirectCurrent", StimulationModeCodes.DirectCurrent, "经颅直流电刺激", "━", "经颅直流电刺激参数设置", RequiresImpedanceMonitoring: true),
        new(FeatureKeys.StimulationPulseCurrent, "TranscranialPulseCurrent", StimulationModeCodes.PulseCurrent, "经颅脉冲电流刺激", "⌁", "经颅脉冲电流刺激参数设置", RequiresImpedanceMonitoring: true),
        new(FeatureKeys.StimulationMonophasicPulseCurrent, "TranscranialMonophasicPulseCurrent", StimulationModeCodes.MonophasicPulseCurrent, "经颅单相脉冲电流刺激", "△", "经颅单相脉冲电流刺激参数设置", RequiresImpedanceMonitoring: true)
    ];

    public static StimulationTypeFeatureDefinition GetStimulationType(string modeCode) =>
        StimulationTypes.FirstOrDefault(item => string.Equals(item.ModeCode, modeCode, StringComparison.Ordinal))
        ?? throw new ArgumentOutOfRangeException(nameof(modeCode), modeCode, "Unknown stimulation mode code.");

    public static IReadOnlySet<string> AllKeys { get; } = Navigation.Select(item => item.Key)
        .Concat(StimulationTypes.Select(item => item.Key)).ToHashSet(StringComparer.Ordinal);

    public static bool DefaultVisibility(string key) =>
        Navigation.FirstOrDefault(item => item.Key == key)?.DefaultVisible
        ?? StimulationTypes.FirstOrDefault(item => item.Key == key)?.DefaultVisible
        ?? throw new ArgumentOutOfRangeException(nameof(key), key, "未知功能 Key");
}
