namespace RuinaoSoftwareWpf;

public sealed class FeatureVisibilityOptionViewModel : ObservableObject
{
    private string displayName;
    private bool isVisible;

    public FeatureVisibilityOptionViewModel(
        string key,
        string localizationKey,
        string orderText,
        string displayName,
        string shortName,
        bool isVisible)
    {
        Key = key;
        LocalizationKey = localizationKey;
        OrderText = orderText;
        this.displayName = displayName;
        ShortName = shortName;
        this.isVisible = isVisible;
    }

    public string Key { get; }
    public string LocalizationKey { get; }
    public string OrderText { get; }
    public string ShortName { get; }
    public string DisplayName { get => displayName; set => SetProperty(ref displayName, value); }
    public bool IsVisible { get => isVisible; set => SetProperty(ref isVisible, value); }
}
