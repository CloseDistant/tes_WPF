namespace RuinaoSoftwareWpf;

public sealed record PrescriptionStimulationModeChoice(string ShortName, string DisplayName, string IconGlyph)
{
    public static PrescriptionStimulationModeChoice Create(string shortName)
    {
        var definition = FeatureCatalog.StimulationTypes.FirstOrDefault(
            item => string.Equals(item.ModeCode, shortName, StringComparison.Ordinal));
        return definition is null
            ? new(shortName, shortName, "—")
            : new(shortName, definition.DisplayName, definition.IconGlyph);
    }
}
