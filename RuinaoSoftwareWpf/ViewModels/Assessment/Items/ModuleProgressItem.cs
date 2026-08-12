namespace RuinaoSoftwareWpf;

using System.Windows.Media;

public sealed class ModuleProgressItem : ObservableObject
{
    private static readonly Brush CompletedBrush = new SolidColorBrush(Color.FromRgb(78, 224, 133));
    private static readonly Brush CurrentBrush = new SolidColorBrush(Color.FromRgb(208, 144, 62));
    private static readonly Brush PendingBrush = new SolidColorBrush(Color.FromRgb(65, 73, 91));
    private static readonly Brush CompletedTextBrush = new SolidColorBrush(Color.FromRgb(202, 244, 217));
    private static readonly Brush CurrentTextBrush = new SolidColorBrush(Color.FromRgb(238, 240, 246));
    private static readonly Brush PendingTextBrush = new SolidColorBrush(Color.FromRgb(142, 150, 168));
    private static readonly Brush CurrentBackgroundBrush = new SolidColorBrush(Color.FromRgb(36, 42, 54));
    private static readonly Brush TransparentBrush = Brushes.Transparent;

    private string statusText = "待完成";
    private Brush dotBrush = PendingBrush;
    private Brush titleBrush = PendingTextBrush;
    private Brush statusBrush = PendingTextBrush;
    private Brush backgroundBrush = TransparentBrush;
    private Brush borderBrush = TransparentBrush;
    private string name;

    public ModuleProgressItem(int index, string code, string displayNameKey, string name, bool isDevelopmentOnly)
    {
        Index = index;
        Code = code;
        DisplayNameKey = displayNameKey;
        this.name = name;
        IsDevelopmentOnly = isDevelopmentOnly;
    }

    public int Index { get; }

    public int DisplayIndex => Index + 1;

    public string Code { get; }

    public string DisplayNameKey { get; }

    public bool IsDevelopmentOnly { get; }

    public string Name
    {
        get => name;
        private set => SetProperty(ref name, value);
    }

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public Brush DotBrush
    {
        get => dotBrush;
        private set => SetProperty(ref dotBrush, value);
    }

    public Brush TitleBrush
    {
        get => titleBrush;
        private set => SetProperty(ref titleBrush, value);
    }

    public Brush StatusBrush
    {
        get => statusBrush;
        private set => SetProperty(ref statusBrush, value);
    }

    public Brush BackgroundBrush
    {
        get => backgroundBrush;
        private set => SetProperty(ref backgroundBrush, value);
    }

    public Brush BorderBrush
    {
        get => borderBrush;
        private set => SetProperty(ref borderBrush, value);
    }

    public void UpdateName(string displayName)
    {
        Name = displayName;
    }

    public void UpdateState(
        int currentIndex,
        bool currentCompleted,
        bool currentSaving,
        bool currentFailed,
        string completedText,
        string currentText,
        string pendingText)
    {
        if (Index < currentIndex)
        {
            StatusText = completedText;
            DotBrush = CompletedBrush;
            TitleBrush = CompletedTextBrush;
            StatusBrush = CompletedTextBrush;
            BackgroundBrush = TransparentBrush;
            BorderBrush = TransparentBrush;
            return;
        }

        if (Index == currentIndex)
        {
            StatusText = currentCompleted
                ? completedText
                : currentFailed
                    ? "保存失败"
                    : currentSaving
                        ? "保存中"
                        : currentText;
            DotBrush = currentCompleted ? CompletedBrush : CurrentBrush;
            TitleBrush = currentCompleted ? CompletedTextBrush : CurrentTextBrush;
            StatusBrush = currentCompleted ? CompletedTextBrush : CurrentBrush;
            BackgroundBrush = CurrentBackgroundBrush;
            BorderBrush = CurrentBrush;
            return;
        }

        StatusText = pendingText;
        DotBrush = PendingBrush;
        TitleBrush = PendingTextBrush;
        StatusBrush = PendingTextBrush;
        BackgroundBrush = TransparentBrush;
        BorderBrush = TransparentBrush;
    }
}
