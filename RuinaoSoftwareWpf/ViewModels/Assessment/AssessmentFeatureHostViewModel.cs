namespace RuinaoSoftwareWpf;

using RuinaoSoftwareWpf.ApplicationContracts;

/// <summary>
/// 在极简评估入口和现有采集工作台之间切换，不承载模块业务。
/// </summary>
public sealed class AssessmentFeatureHostViewModel : ObservableObject
{
    private ObservableObject currentContent;

    public AssessmentFeatureHostViewModel(
        AssessmentEntryViewModel entry,
        AssessmentCaptureViewModel workbench)
    {
        Entry = entry;
        Workbench = workbench;
        currentContent = entry;
        Entry.RunActivated += OnRunActivated;
    }

    public AssessmentEntryViewModel Entry { get; }

    public AssessmentCaptureViewModel Workbench { get; }

    public ObservableObject CurrentContent
    {
        get => currentContent;
        private set => SetProperty(ref currentContent, value);
    }

    public bool IsWorkbenchVisible => ReferenceEquals(CurrentContent, Workbench);

    public void ShowEntry()
    {
        CurrentContent = Entry;
        OnPropertyChanged(nameof(IsWorkbenchVisible));
    }

    public async Task ShowEntryAsync(CancellationToken cancellationToken = default)
    {
        ShowEntry();
        await Entry.LoadAsync(cancellationToken);
    }

    private void OnRunActivated(object? sender, AssessmentRunContext run)
    {
        Workbench.ConfigureFormalRun(run);
        CurrentContent = Workbench;
        OnPropertyChanged(nameof(IsWorkbenchVisible));
    }
}
