namespace RuinaoSoftwareWpf;

public sealed record StimulationTypeFeatureDefinition(
    string Key,
    string LocalizationKey,
    string ModeCode,
    string DisplayName,
    string IconGlyph,
    string FooterStatus,
    bool RequiresImpedanceMonitoring = false,
    StimulationModeExecutionAvailability ExecutionAvailability = StimulationModeExecutionAvailability.Hardware,
    bool DefaultVisible = true)
{
    public string ShortName => ModeCode;
}
