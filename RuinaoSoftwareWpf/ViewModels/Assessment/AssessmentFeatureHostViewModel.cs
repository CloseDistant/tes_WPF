namespace RuinaoSoftwareWpf;

using RuinaoSoftwareWpf.ApplicationContracts;

/// <summary>
/// 在极简评估入口和现有采集工作台之间切换，不承载模块业务。
/// </summary>
public sealed class AssessmentFeatureHostViewModel : ObservableObject
{
    private ObservableObject currentContent;
    private TaskCompletionSource? matchingCompletion;

    public AssessmentFeatureHostViewModel(
        AssessmentEntryViewModel entry,
        AssessmentCaptureViewModel workbench,
        AssessmentPatientMatchingViewModel matching)
    {
        Entry = entry;
        Workbench = workbench;
        Matching = matching;
        currentContent = entry;
        Entry.RunActivated += OnRunActivated;
        Matching.BackRequested += OnMatchingBackRequested;
        Matching.FollowUpSelected += OnMatchingFollowUpSelected;
    }

    public AssessmentEntryViewModel Entry { get; }

    public AssessmentCaptureViewModel Workbench { get; }

    public AssessmentPatientMatchingViewModel Matching { get; }

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

    public void ShowMatching()
    {
        CurrentContent = Matching;
        OnPropertyChanged(nameof(IsWorkbenchVisible));
    }

    public async Task ShowMatchingAsync(CancellationToken cancellationToken = default)
    {
        Matching.PrepareForManualQuery();
        ShowMatching();
        matchingCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            // 进入匹配页面不自动请求接口，由操作人员手动点击“查询”。
            await matchingCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            matchingCompletion = null;
        }
    }

    private void OnRunActivated(object? sender, AssessmentRunContext run)
    {
        Workbench.ConfigureFormalRun(run);
        CurrentContent = Workbench;
        OnPropertyChanged(nameof(IsWorkbenchVisible));
    }

    private void OnMatchingBackRequested(object? sender, EventArgs e)
    {
        ShowEntry();
        matchingCompletion?.TrySetResult();
        matchingCompletion = null;
    }

    private void OnMatchingFollowUpSelected(object? sender, ExternalFollowUpDetail detail)
    {
        if (detail.Id is not long detailId)
        {
            return;
        }

        Entry.SetMatchedFollowUp(detailId);
        Workbench.SetMatchedFollowUp(detailId);
        ShowEntry();
        matchingCompletion?.TrySetResult();
        matchingCompletion = null;
    }
}
