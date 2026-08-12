namespace RuinaoSoftwareWpf;

public interface ISoftwareActivationService
{
    bool IsActivated { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<SoftwareActivationResult> ActivateAsync(
        string activationCode,
        CancellationToken cancellationToken = default);
}
