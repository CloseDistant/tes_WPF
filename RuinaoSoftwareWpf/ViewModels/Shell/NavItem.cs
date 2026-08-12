namespace RuinaoSoftwareWpf;

public sealed class NavItem : ObservableObject
{
    private string text;
    private bool isSelected;

    public NavItem(AppPage page, string text)
    {
        Page = page;
        this.text = text;
    }

    public AppPage Page { get; }
    public string Text { get => text; set => SetProperty(ref text, value); }
    public bool IsSelected { get => isSelected; set => SetProperty(ref isSelected, value); }
}
