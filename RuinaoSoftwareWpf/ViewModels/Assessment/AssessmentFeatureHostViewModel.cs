namespace RuinaoSoftwareWpf;

using RuinaoSoftwareWpf.ApplicationContracts;

/// <summary>
/// 在患者端欢迎/匹配页、极简评估入口和现有采集工作台之间切换，不承载模块业务。
/// </summary>
public sealed class AssessmentFeatureHostViewModel : ObservableObject
{
    private ObservableObject currentContent;
    private TaskCompletionSource? matchingCompletion;
    private TaskCompletionSource? patientPortalCompletion;
    private bool patientPortalReturnsToEntry;

    public AssessmentFeatureHostViewModel(
        AssessmentEntryViewModel entry,
        AssessmentCaptureViewModel workbench,
        AssessmentPatientMatchingViewModel matching,
        AssessmentPatientWelcomeViewModel patientWelcome,
        AssessmentPatientPortalViewModel patientPortal)
    {
        Entry = entry;
        Workbench = workbench;
        Matching = matching;
        PatientWelcome = patientWelcome;
        PatientPortal = patientPortal;
        currentContent = entry;
        Entry.RunActivated += OnRunActivated;
        Matching.BackRequested += OnMatchingBackRequested;
        Matching.FollowUpSelected += OnMatchingFollowUpSelected;
        PatientWelcome.EnterRequested += OnPatientWelcomeEnterRequested;
        PatientPortal.BackRequested += OnPatientPortalBackRequested;
        PatientPortal.FollowUpSelected += OnPatientPortalFollowUpSelected;
    }

    public AssessmentEntryViewModel Entry { get; }

    public AssessmentCaptureViewModel Workbench { get; }

    public AssessmentPatientMatchingViewModel Matching { get; }

    public AssessmentPatientWelcomeViewModel PatientWelcome { get; }

    public AssessmentPatientPortalViewModel PatientPortal { get; }

    public ObservableObject CurrentContent
    {
        get => currentContent;
        private set => SetProperty(ref currentContent, value);
    }

    public bool IsWorkbenchVisible => ReferenceEquals(CurrentContent, Workbench);

    public void ShowEntry()
    {
        CompletePendingMatching();
        CompletePendingPatientPortal();
        patientPortalReturnsToEntry = false;
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

    /// <summary>
    /// 打开患者端欢迎页。正式采集入口保持现有页面，不在这里重排或替换。
    /// </summary>
    public void ShowPatientWelcome()
    {
        CompletePendingMatching();
        CompletePendingPatientPortal();
        patientPortalReturnsToEntry = false;
        CurrentContent = PatientWelcome;
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

    /// <summary>
    /// 从已有患者的评估入口打开患者视角手机号精确查询页。
    /// 查询页完成返回或选择随访后回到同一个评估入口，令入口命令正常结束。
    /// </summary>
    public async Task ShowPatientPortalFromEntryAsync(CancellationToken cancellationToken = default)
    {
        CompletePendingMatching();
        PatientPortal.Reset();
        patientPortalReturnsToEntry = true;
        CurrentContent = PatientPortal;
        OnPropertyChanged(nameof(IsWorkbenchVisible));
        patientPortalCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await patientPortalCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            patientPortalCompletion = null;
            patientPortalReturnsToEntry = false;
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
    }

    private void OnPatientWelcomeEnterRequested(object? sender, EventArgs e)
    {
        patientPortalReturnsToEntry = false;
        PatientPortal.Reset();
        CurrentContent = PatientPortal;
        OnPropertyChanged(nameof(IsWorkbenchVisible));
    }

    private void OnPatientPortalBackRequested(object? sender, EventArgs e)
    {
        if (patientPortalReturnsToEntry)
        {
            ShowEntry();
            return;
        }

        ShowPatientWelcome();
    }

    private async void OnMatchingFollowUpSelected(object? sender, ExternalFollowUpDetail detail)
    {
        if (detail.Id is not long detailId)
        {
            return;
        }

        Entry.SetMatchedFollowUp(detailId);
        Workbench.SetMatchedFollowUp(detailId);
        await RefreshEntryBeforeReturningAsync();
        ShowEntry();
    }

    private async void OnPatientPortalFollowUpSelected(object? sender, ExternalFollowUpDetail detail)
    {
        if (detail.Id is not long detailId)
        {
            return;
        }

        Entry.SetMatchedFollowUp(detailId);
        Workbench.SetMatchedFollowUp(detailId);
        await RefreshEntryBeforeReturningAsync();
        ShowEntry();
    }

    /// <summary>
    /// 随访匹配只改变当前患者的随访上下文，不创建评估。
    /// 返回入口前先读取本地进行中的 Run，避免页面先显示“开始新的评估”或暂时禁用按钮，
    /// 必须切换左侧模块后才恢复为“继续评估”。
    /// </summary>
    private Task RefreshEntryBeforeReturningAsync()
    {
        return Entry.LoadAsync(CancellationToken.None);
    }

    /// <summary>
    /// 匹配页面可能被左侧导航或视角切换直接离开，不能让入口命令永久等待。
    /// 完成信号只表示“匹配流程已离开”，不会伪造患者选择结果。
    /// </summary>
    private void CompletePendingMatching()
    {
        matchingCompletion?.TrySetResult();
        matchingCompletion = null;
    }

    private void CompletePendingPatientPortal()
    {
        patientPortalCompletion?.TrySetResult();
        patientPortalCompletion = null;
    }
}
