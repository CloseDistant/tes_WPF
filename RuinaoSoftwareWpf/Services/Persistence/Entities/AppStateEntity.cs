namespace RuinaoSoftwareWpf;

internal sealed class AppStateEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public long UpdatedAtUnixMs { get; set; }
}
