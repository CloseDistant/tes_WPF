namespace RuinaoSoftwareWpf;

internal interface IReleaseIntegrityStateStore
{
    Task<ReleaseIntegritySnapshot?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(
        ReleaseIntegritySnapshot snapshot,
        CancellationToken cancellationToken = default);
}
