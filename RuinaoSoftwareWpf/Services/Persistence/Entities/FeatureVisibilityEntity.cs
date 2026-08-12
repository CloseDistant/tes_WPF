namespace RuinaoSoftwareWpf;

internal sealed class FeatureVisibilityEntity
{
    public string FeatureKey { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;
    public long? UpdatedByUserId { get; set; }
    public long UpdatedAtUnixMs { get; set; }
}
