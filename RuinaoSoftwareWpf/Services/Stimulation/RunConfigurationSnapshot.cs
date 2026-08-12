namespace RuinaoSoftwareWpf;

public sealed record RunConfigurationSnapshot(
    string SessionKey,
    string ModuleCode,
    long Version,
    DateTimeOffset CapturedAt,
    string Json);
