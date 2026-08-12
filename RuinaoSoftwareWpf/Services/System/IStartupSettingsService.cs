namespace RuinaoSoftwareWpf;

public interface IStartupSettingsService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    bool AutoConnectOnStartup { get; }

    Task SaveAutoConnectOnStartupAsync(
        bool enabled,
        CancellationToken cancellationToken = default);
}
