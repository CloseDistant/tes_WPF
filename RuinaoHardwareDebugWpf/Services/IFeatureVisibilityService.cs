namespace RuinaoHardwareDebugWpf;

public interface IFeatureVisibilityService
{
    event EventHandler? VisibilityChanged;

    Task InitializeAsync(CancellationToken cancellationToken = default);

    bool IsVisible(string featureKey);

    Task SaveAsync(
        IReadOnlyDictionary<string, bool> visibility,
        CancellationToken cancellationToken = default);
}
