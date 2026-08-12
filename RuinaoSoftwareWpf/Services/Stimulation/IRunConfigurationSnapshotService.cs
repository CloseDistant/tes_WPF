namespace RuinaoSoftwareWpf;

public interface IRunConfigurationSnapshotService
{
    RunConfigurationSnapshot Capture<T>(string sessionKey, string moduleCode, T configuration);
    RunConfigurationSnapshot? GetCurrent(string moduleCode);
    void Clear(string moduleCode);
}
