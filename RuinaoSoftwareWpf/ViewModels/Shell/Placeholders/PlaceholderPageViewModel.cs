namespace RuinaoSoftwareWpf;

/// <summary>未实现页面占位 ViewModel。</summary>
public sealed class PlaceholderPageViewModel : ObservableObject
{
    private string title = string.Empty;
    private string description = string.Empty;

    public string Title
    {
        get => title;
        private set => SetProperty(ref title, value);
    }

    public string Description
    {
        get => description;
        private set => SetProperty(ref description, value);
    }

    public void Configure(string pageTitle, string pageDescription)
    {
        Title = pageTitle;
        Description = pageDescription;
    }
}
