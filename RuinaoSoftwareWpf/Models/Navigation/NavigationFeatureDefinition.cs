namespace RuinaoSoftwareWpf;

public sealed record NavigationFeatureDefinition(
    string Key,
    AppPage Page,
    string LocalizationKey,
    bool DefaultVisible = true);
