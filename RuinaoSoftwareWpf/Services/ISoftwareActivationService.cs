namespace RuinaoSoftwareWpf;

public sealed record SoftwareActivationResult(
    bool Succeeded,
    string Message);

public interface ISoftwareActivationService
{
    bool IsActivated { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<SoftwareActivationResult> ActivateAsync(
        string activationCode,
        CancellationToken cancellationToken = default);
}
