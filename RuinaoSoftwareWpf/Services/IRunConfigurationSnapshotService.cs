namespace RuinaoSoftwareWpf;

public sealed record RunConfigurationSnapshot(
    string SessionKey,
    string ModuleCode,
    long Version,
    DateTimeOffset CapturedAt,
    string Json);

public interface IRunConfigurationSnapshotService
{
    RunConfigurationSnapshot Capture<T>(string sessionKey, string moduleCode, T configuration);
    RunConfigurationSnapshot? GetCurrent(string moduleCode);
    void Clear(string moduleCode);
}
