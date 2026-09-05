namespace RuinaoSoftwareWpf;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RuinaoSoftwareWpf.ApplicationContracts;

/// <summary>
/// 采集工作台 ViewModel。
/// 负责采集模块流程状态、中央显示区状态、按钮显隐、模块进度和录制状态展示。
/// 摄像头预览、音视频录制和数据库写入分别由 View 与服务层处理。
/// </summary>
public sealed partial class AssessmentCaptureViewModel : ObservableObject, IAssessmentActivityState
{

    private readonly ILocalizationService localization;
    private readonly IUserDialogService userDialogService;
    private readonly IModuleEventRecorder moduleEventRecorder;
    private readonly IUnifiedSessionService unifiedSessionService;
    private readonly ApplicationContracts.IAssessmentModule assessmentModuleLifecycle;
    private readonly IPatientService patientService;
    private readonly IToastService toastService;
    private readonly TimeProvider timeProvider;
    private readonly AssessmentWorkbenchCoordinator workbenchCoordinator;
    private readonly ICameraCaptureService cameraCaptureService;
    private readonly ICaptureMediaService captureMediaService;
    private readonly ICaptureFormRecordService captureFormRecordService;
    private readonly FaceConditionMonitor faceConditionMonitor = new(
        TimeSpan.FromSeconds(3),
        Stopwatch.Frequency);
    private readonly FaceReadinessMonitor faceReadinessMonitor = new(
        TimeSpan.FromSeconds(1),
        Stopwatch.Frequency);
    private long? matchedFollowUpId;
    private readonly DispatcherTimer calibrationTimer = new();
    private readonly DispatcherTimer pictureBrowseTimer = new();
    private readonly DispatcherTimer videoBrowseTimer = new();
    private readonly DispatcherTimer voiceBaselineTimer = new();
    private readonly DispatcherTimer wordReadingTimer = new();
    private readonly DispatcherTimer syncTestTimer = new();
    private readonly EyeCalibrationSequenceFactory calibrationSequenceFactory;
    private readonly Random videoBrowseRandom = new();
    private readonly Queue<CalibrationFrame> calibrationFrames = new();
    private CalibrationFrame? activeCalibrationFrame;
    private DateTimeOffset? activeCalibrationFrameStartedAt;
    private static readonly Brush ActiveStepBrush = new SolidColorBrush(Color.FromRgb(208, 144, 62));
    private static readonly Brush DemoStepBrush = new SolidColorBrush(Color.FromRgb(75, 119, 216));
    private static readonly Brush InactiveStepBrush = new SolidColorBrush(Color.FromRgb(48, 54, 69));
    private static readonly Brush ActiveTextBrush = new SolidColorBrush(Color.FromRgb(228, 232, 239));
    private static readonly Brush InactiveTextBrush = new SolidColorBrush(Color.FromRgb(142, 150, 168));
    private static readonly Brush FaceNotReadyBrush = new SolidColorBrush(Color.FromRgb(240, 93, 94));
    private static readonly Brush FaceStabilizingBrush = new SolidColorBrush(Color.FromRgb(217, 154, 58));
    private static readonly Brush FaceReadyBrush = new SolidColorBrush(Color.FromRgb(85, 217, 139));
    private CaptureWorkbenchStep currentStep
    {
        get => (CaptureWorkbenchStep)workbenchCoordinator.CurrentStepIndex;
        set => workbenchCoordinator.CurrentStepIndex = (int)value;
    }

    private int currentDevStepIndex
    {
        get => (int)currentStep;
        set => currentStep = Enum.IsDefined(typeof(CaptureWorkbenchStep), value)
            ? (CaptureWorkbenchStep)value
            : CaptureWorkbenchStep.Demo;
    }

    private void MoveToStep(CaptureWorkbenchStep step)
    {
        if (step == CaptureWorkbenchStep.Completed
            && !IsFormModule
            && captureMediaService.IsCapturing)
        {
            BeginModuleDataSaving();
            return;
        }

        if (currentStep != step)
        {
            ResetFaceReadiness();
            currentStep = step;
        }
    }
    private bool isDemoCompleted;
    private bool isDemoPlaying;
    private int currentModuleIndex
    {
        get => workbenchCoordinator.CurrentModuleIndex;
        set => workbenchCoordinator.CurrentModuleIndex = value;
    }
    private string selectedCameraDevice = "未选择摄像头";
    private string cameraStatusText = "请选择摄像头";
    private string playbackTimeText = "00:00 / 未播放";
    private string calibrationText = "+";
    private double calibrationX = 50;
    private double calibrationY = 50;
    private string calibrationMarkerColor = "#969DA8";
    private bool isCalibrationMarkerVisible;
    private int calibrationMoveDurationMilliseconds;
    private int calibrationAnimationSequence;
    private int calibrationTrialIndex = 1;
    private string frameSaveStatusText = string.Empty;
    private string frameOutputDirectory = string.Empty;
    private string stageNoticeText = string.Empty;
    private FaceReadinessState faceReadinessState = FaceReadinessState.NotReady;
    private double faceReadinessProgressPercent;
    private double faceReadinessRemainingSeconds = 1;
    private string faceReadinessReasonText = string.Empty;
    private string pictureBrowseImagePath = string.Empty;
    private string pictureBrowseStatusText = "待开始";
    private string pictureBrowseRestText = string.Empty;
    private PictureBrowsePhase pictureBrowsePhase = PictureBrowsePhase.Idle;
    private PictureBrowseSequenceItem[] pictureBrowseItems = [];
    private string pictureBrowseVersion = string.Empty;
    private int pictureBrowseIndex;
    private int pictureBrowseRestRemainingSeconds;
    private bool pictureBrowseRestPaused;
    private DateTimeOffset? pictureBrowseFixationStartedAt;
    private DateTimeOffset? pictureBrowseImageStartedAt;
    private DateTimeOffset? pictureBrowseRestStartedAt;
    private DateTimeOffset? pictureBrowseFinalBlankStartedAt;
    private int? currentPictureBrowseImageType;
    private VideoBrowsePhase videoBrowsePhase = VideoBrowsePhase.Idle;
    private VideoBrowseItem[] videoBrowseItems = [];
    private int videoBrowseIndex;
    private int videoBrowseRestRemainingSeconds;
    private string videoBrowseVideoPath = string.Empty;
    private string videoBrowseStatusText = "待开始";
    private string videoBrowseRestText = string.Empty;
    private int? currentVideoBrowseVideoType;
    private DateTimeOffset? currentVideoBrowseStartedAt;
    private VoiceBaselinePhase voiceBaselinePhase = VoiceBaselinePhase.Idle;
    private int voiceBaselineIndex;
    private int voiceBaselineRemainingSeconds;
    private DateTimeOffset? currentVoiceBaselineStartedAt;
    private DateTimeOffset? voiceBaselineDetectionWindowStartedAt;
    private DateTimeOffset? voiceBaselineDetectionWindowEndedAt;
    private DateTimeOffset? voiceBaselineVoiceDetectedAt;
    private bool voiceBaselineHasVoice;
    private bool voiceBaselineVoiceDetectionFinalized;
    private bool voiceBaselineMediaFinalizing;
    private long? voiceBaselineActiveMediaSessionId;
    private string voiceBaselineStatusText = string.Empty;
    private string voiceBaselineRestText = string.Empty;
    private WordReadingPhase wordReadingPhase = WordReadingPhase.Idle;
    private int wordReadingIndex;
    private int wordReadingRemainingSeconds;
    private DateTimeOffset? currentWordReadingStartedAt;
    private string wordReadingStatusText = string.Empty;
    private string wordReadingRestText = string.Empty;
    private string selectedBasicInfoGender = string.Empty;
    private string basicInfoBirthDateText = string.Empty;
    private string selectedBasicInfoEducation = string.Empty;
    private string selectedBasicInfoOccupation = string.Empty;
    private string selectedBasicInfoIncomeLevel = string.Empty;
    private string basicInfoValidationMessage = string.Empty;
    private string basicInfoSaveStatusText = string.Empty;
    private bool isBasicInfoOptionPanelOpen;
    private string basicInfoOptionField = string.Empty;
    private string basicInfoOptionTitle = string.Empty;
    private bool isQuestionnaireOptionPanelOpen;
    private QuestionnaireQuestionItem? selectedQuestionnaireQuestion;
    private string questionnaireOptionTitle = string.Empty;
    private string questionnaireValidationMessage = string.Empty;
    private string questionnaireSaveStatusText = string.Empty;
    private readonly QuestionnaireSessionState questionnaireSession = new();
    private int syncTestRemainingSeconds = SyncTestDurationSeconds;
    private bool isSyncTestRunning;
    private int savedFrameCount;
    private AssessmentModuleRunContext? activeModuleAttempt;
    private Task pendingLifecycleOperation = Task.CompletedTask;
    private bool isModuleSaveFailed;
    private bool isWorkbenchVisible;
    private AssessmentRunContext? activeRun;
    private AssessmentExecutionMode executionMode = AssessmentExecutionMode.Formal;
    private long? activeDevelopmentMediaSessionId;
    private string? activeDevelopmentMediaModuleCode;

    private static int FormalModuleCount => FormalModuleFlowDefinitions.Count;

    public static int TotalFormalModuleCount => FormalModuleCount;

#if DEBUG
    public bool IsDevelopmentModuleNavigationEnabled => true;
#else
    public bool IsDevelopmentModuleNavigationEnabled => false;
#endif

    public bool IsDevelopmentModuleOverride => executionMode == AssessmentExecutionMode.DevelopmentDirect;

    public AssessmentCaptureViewModel(
        ICaptureMediaService captureMediaService,
        ICaptureFormRecordService captureFormRecordService,
        ICameraCaptureService cameraCaptureService,
        ILocalizationService localization,
        IUserDialogService userDialogService,
        IModuleEventRecorder moduleEventRecorder,
        IUnifiedSessionService unifiedSessionService,
        ApplicationContracts.IAssessmentModule assessmentModuleLifecycle,
        IPatientService patientService,
        IToastService toastService,
        AssessmentWorkbenchCoordinator workbenchCoordinator,
        TimeProvider timeProvider)
    {
        this.captureMediaService = captureMediaService;
        this.captureFormRecordService = captureFormRecordService;
        this.cameraCaptureService = cameraCaptureService;
        this.localization = localization;
        this.userDialogService = userDialogService;
        this.moduleEventRecorder = moduleEventRecorder;
        this.unifiedSessionService = unifiedSessionService;
        this.assessmentModuleLifecycle = assessmentModuleLifecycle;
        this.patientService = patientService;
        this.toastService = toastService;
        this.workbenchCoordinator = workbenchCoordinator;
        this.timeProvider = timeProvider;
        patientService.CurrentPatientChanged += (_, _) => ClearMatchedFollowUpForPatientChange();
        calibrationSequenceFactory = new EyeCalibrationSequenceFactory(new Random());
        this.localization.LanguageChanged += (_, _) =>
        {
            RefreshModuleDisplayNames();
            NotifyStageChanged();
        };
        captureMediaService.Completed += OnRecordingCompleted;
        captureMediaService.AudioLevelAvailable += OnAudioLevelAvailable;
        DevNextStepCommand = new RelayCommand(_ => MoveToNextDevStep());
        GoNextModuleCommand = new AsyncRelayCommand(_ => GoNextModuleAsync());
        RetryFailedModuleCommand = new RelayCommand(_ => ResetFailedModule());
        SwitchModuleCommand = new AsyncRelayCommand(
            (parameter, cancellationToken) => SwitchModuleAsync(parameter, cancellationToken),
            parameter => IsDevelopmentModuleNavigationEnabled && parameter is ModuleProgressItem);
        RefreshCameraDevicesCommand = new RelayCommand(_ => LoadCameraDevices());
        SubmitBasicInfoCommand = new AsyncRelayCommand(_ => SubmitBasicInfoAsync());
        OpenBasicInfoOptionCommand = new RelayCommand(OpenBasicInfoOptionPanel);
        SelectBasicInfoOptionCommand = new RelayCommand(SelectBasicInfoOption);
        CloseBasicInfoOptionCommand = new RelayCommand(_ => CloseBasicInfoOptionPanel());
        OpenQuestionnaireOptionCommand = new RelayCommand(OpenQuestionnaireOptionPanel);
        SelectQuestionnaireOptionCommand = new RelayCommand(SelectQuestionnaireOption);
        CloseQuestionnaireOptionCommand = new RelayCommand(_ => CloseQuestionnaireOptionPanel());
        PreviousQuestionnaireQuestionCommand = new RelayCommand(_ => GoToPreviousQuestionnaireQuestion());
        NextQuestionnaireQuestionCommand = new RelayCommand(_ => GoToNextQuestionnaireQuestion());
        SubmitQuestionnaireCommand = new AsyncRelayCommand(_ => SubmitQuestionnaireAsync());
        StartShortTextReadingCommand = new AsyncRelayCommand(
            StartShortTextReadingActionAsync,
            () => CanExecuteShortTextReadingAction,
            exception => ShowStageNotice($"短文朗读启动失败：{exception.Message}"));
        InitializeEmotionQuestionModule();
        InitializeDotProbeModule();
        InitializeEmotionOddballModule();
        InitializeEmotionLetterSearchModule();
        InitializeEmotionStroopModule();
        LoadModuleProgressItems();
        frameSaveStatusText = T("CaptureWorkspaceRecordingPending");
        basicInfoSaveStatusText = T("CaptureWorkspaceFormPending");
        questionnaireSaveStatusText = T("CaptureWorkspaceFormPending");
        voiceBaselineStatusText = T("CaptureWorkspaceRecordingPending");
        wordReadingStatusText = T("CaptureWorkspaceRecordingPending");
        shortTextReadingStatusText = T("CaptureWorkspaceRecordingPending");
        selectedCameraDevice = T("CaptureWorkspaceNoCameraSelected");
        cameraStatusText = T("CaptureWorkspaceChooseCamera");
        CameraDevices.Add(T("CaptureWorkspaceNoCameraDetected"));
        selectedCameraDevice = CameraDevices[0];
        LoadCurrentQuestionnaireQuestions();
        calibrationTimer.Tick += (_, _) => ShowNextCalibrationFrame();
        pictureBrowseTimer.Tick += (_, _) => AdvancePictureBrowse();
        videoBrowseTimer.Tick += (_, _) => AdvanceVideoBrowseAfterBlank();
        voiceBaselineTimer.Interval = TimeSpan.FromSeconds(1);
        voiceBaselineTimer.Tick += (_, _) => AdvanceVoiceBaseline();
        wordReadingTimer.Interval = TimeSpan.FromSeconds(1);
        wordReadingTimer.Tick += (_, _) => AdvanceWordReading();
        // Use a finer UI tick so the displayed whole-second countdown is painted
        // before each boundary instead of occasionally skipping the first value.
        shortTextReadingTimer.Interval = TimeSpan.FromMilliseconds(16);
        shortTextReadingTimer.Tick += (_, _) => AdvanceShortTextReading();
        syncTestTimer.Interval = TimeSpan.FromSeconds(1);
        syncTestTimer.Tick += (_, _) => AdvanceSyncTest();
        LoadCameraDevices();
    }

    internal bool IsCameraOpen => cameraCaptureService.IsOpen;

    internal Task<bool> OpenCameraAsync(
        int preferredIndex,
        bool forceReopen = false,
        CancellationToken cancellationToken = default)
    {
        return cameraCaptureService.OpenAsync(
            preferredIndex,
            SelectedCameraDevice,
            forceReopen,
            cancellationToken);
    }

    internal bool TryTakeLatestCameraPreview(out CameraPreviewSnapshot snapshot)
    {
        return cameraCaptureService.TryTakeLatestPreview(out snapshot);
    }

    internal bool TryTakeLatestCameraFaceStatus(out CameraFaceStatusSnapshot snapshot)
    {
        return cameraCaptureService.TryTakeLatestFaceStatus(out snapshot);
    }

    internal void SetCameraPreviewRenderingEnabled(bool enabled)
    {
        cameraCaptureService.SetPreviewRenderingEnabled(enabled);
    }

    internal string CameraOpenFailureMessage => cameraCaptureService.LastOpenFailureMessage
        ?? T("CaptureWorkspaceCameraOpenFailed");

    internal Task CloseCameraAsync(CancellationToken cancellationToken = default)
    {
        return cameraCaptureService.CloseAsync(cancellationToken);
    }

    public void ReleaseCameraForNavigation()
    {
        isWorkbenchVisible = false;
        if (captureMediaService.IsCapturing)
        {
            captureMediaService.RequestStop(
                CaptureMediaStopReason.Discarded,
                "用户离开采集工作台，当前模块尝试已取消。");
        }

        if (activeModuleAttempt is { } attempt)
        {
            if (!captureMediaService.IsCapturing && IsFormModule)
            {
                activeModuleAttempt = null;
                pendingLifecycleOperation = RunLifecycleOperationAsync(
                    pendingLifecycleOperation,
                    () => assessmentModuleLifecycle.CancelAsync(
                        attempt.AttemptId,
                        "用户离开表单页面，未提交内容已作废。"));
                if (IsBasicInfoModule)
                {
                    ResetBasicInfoFormState(clearValues: true);
                }
                else
                {
                    ResetQuestionnaireState(clearAnswers: true);
                }
            }
        }

        NotifyStageChanged();
    }

    public async Task<string> GetOrStartUnifiedSessionKeyAsync(CancellationToken cancellationToken = default)
    {
        if (patientService.CurrentPatient is null)
        {
            userDialogService.ShowInformation("数字表型采集", "请先新增或选择患者，再开始数字表型采集。");
            throw new InvalidOperationException("数字表型采集需要当前患者信息。");
        }

        return (await unifiedSessionService.GetOrStartAsync(cancellationToken)).SessionKey;
    }

    public void ConfigureFormalRun(AssessmentRunContext run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.ModuleFlow.Count == 0 || run.NextModuleTypeId is not int nextModuleTypeId)
        {
            throw new InvalidOperationException("当前评估没有可继续执行的模块。");
        }

        LoadFormalRunModuleProgressItems(run.ModuleFlow);
        var nextModuleIndex = ModuleProgressItems
            .Select((item, index) => (item, index))
            .Where(pair => pair.item.ModuleTypeId == nextModuleTypeId)
            .Select(pair => pair.index)
            .DefaultIfEmpty(-1)
            .First();
        if (nextModuleIndex < 0)
        {
            throw new InvalidOperationException($"当前评估的下一模块类型不存在：{nextModuleTypeId}。");
        }

        activeRun = run;
        executionMode = AssessmentExecutionMode.Formal;
        activeDevelopmentMediaSessionId = null;
        activeDevelopmentMediaModuleCode = null;
        activeModuleAttempt = null;
        isModuleSaveFailed = false;
        StopModuleExecutionTimers();
        ResetBasicInfoFormState(clearValues: true);
        ResetQuestionnaireState(clearAnswers: true);

        currentModuleIndex = nextModuleIndex;
        MoveToStep(IsFormModuleCode(CurrentModuleCode)
            ? CaptureWorkbenchStep.ModuleExecution
            : CaptureWorkbenchStep.Demo);
        isDemoCompleted = IsFormModule;
        isDemoPlaying = false;
        ResetFrameSavingStatus();
        StageNoticeText = string.Empty;
        UpdateModuleProgressItems();
        NotifyStageChanged();
    }

    public async Task EnterWorkbenchAsync(CancellationToken cancellationToken = default)
    {
        isWorkbenchVisible = true;
        if (IsFormModule && currentStep == CaptureWorkbenchStep.ModuleExecution)
        {
            await EnsureCurrentModuleAttemptStartedAsync(cancellationToken);
        }

        NotifyStageChanged();
    }

    public async Task<AssessmentModuleRunContext> BeginCurrentModuleAttemptAsync(
        string sessionKey,
        CancellationToken cancellationToken = default)
    {
        if (activeModuleAttempt is not null)
        {
            if (activeModuleAttempt.ModuleTypeId != CurrentModuleTypeId)
            {
                throw new InvalidOperationException("上一模块尝试尚未结束，不能启动其他模块。");
            }

            return activeModuleAttempt;
        }

        var patientCode = await patientService.GetRequiredCurrentPatientCodeAsync(cancellationToken);
        var run = activeRun
            ?? throw new InvalidOperationException("当前没有已由评估入口确认的正式评估，请返回入口开始或继续评估。");
        if (!string.Equals(run.PatientCode, patientCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("当前患者与评估运行不一致，请返回评估入口重新进入。");
        }

        activeModuleAttempt = await assessmentModuleLifecycle.StartAsync(
            new AssessmentModuleStartRequest(
                run.RunId,
                patientCode,
                sessionKey,
                CurrentModuleCode,
                CurrentModule,
                CurrentModuleTypeId,
                CurrentModuleSequence,
                ModuleProgressItems.Count(static item => !item.IsDevelopmentOnly)),
            cancellationToken);
        isModuleSaveFailed = false;
        UpdateModuleProgressItems();
        NotifyStageChanged();
        return activeModuleAttempt;
    }

    public async Task FailCurrentModuleAttemptAsync(
        string errorCode,
        string message,
        CancellationToken cancellationToken = default)
    {
        var attempt = activeModuleAttempt;
        if (attempt is null)
        {
            return;
        }

        await pendingLifecycleOperation.WaitAsync(cancellationToken);
        await assessmentModuleLifecycle.FailAsync(attempt.AttemptId, errorCode, message, cancellationToken);
        activeModuleAttempt = null;
        isModuleSaveFailed = true;
        UpdateModuleProgressItems();
        NotifyStageChanged();
    }

    internal bool IsMediaRecording => captureMediaService.IsCapturing;

    internal async Task<CaptureMediaSession> StartMediaRecordingAsync(
        CaptureMediaStartRequest request,
        CancellationToken cancellationToken = default)
    {
        var session = await captureMediaService.StartAsync(request, cancellationToken);
        if (IsDevelopmentModuleOverride)
        {
            activeDevelopmentMediaSessionId = session.SessionId;
            activeDevelopmentMediaModuleCode = session.ModuleCode;
        }

        return session;
    }

    internal void RequestMediaStop(CaptureMediaStopReason reason, string message)
    {
        captureMediaService.RequestStop(reason, message);
    }

    internal async Task WaitForMediaIdleAsync(CancellationToken cancellationToken = default)
    {
        await captureMediaService.WaitForIdleAsync(cancellationToken);
        await pendingLifecycleOperation.WaitAsync(cancellationToken);
    }

    public string DemoVideoPath => CurrentModuleCode switch
    {
        PictureBrowseModuleCode => ResolveAssetPath("Assets", "CaptureWorkbench", "Videos", "PictureBrowseDemo.mp4"),
        VideoBrowseModuleCode => ResolveAssetPath("Assets", "CaptureWorkbench", "Videos", "VideoBrowseDemo.mp4"),
        VoiceBaselineModuleCode => ResolveAssetPath("Assets", "CaptureWorkbench", "Videos", "VoiceBaselineDemo.mp4"),
        WordReadingModuleCode => ResolveAssetPath("Assets", "CaptureWorkbench", "Videos", "WordReadingDemo.mp4"),
        ShortTextReadingModuleCode => ResolveAssetPath("Assets", "CaptureWorkbench", "Videos", "ShortTextReadingDemo.mp4"),
        EmotionQuestionModuleCode => ResolveAssetPath("Assets", "CaptureWorkbench", "Videos", "EmotionQuestionDemo.mp4"),
        DotProbeModuleCode => ResolveAssetPath("Assets", "CaptureWorkbench", "Videos", "DotProbeDemo.mp4"),
        EmotionOddballModuleCode => ResolveAssetPath("Assets", "CaptureWorkbench", "Videos", "EmotionOddballDemo.mp4"),
        EmotionLetterSearchModuleCode => ResolveAssetPath("Assets", "CaptureWorkbench", "Videos", "EmotionLetterSearchDemo.mp4"),
        EmotionStroopModuleCode => ResolveAssetPath("Assets", "CaptureWorkbench", "Videos", "EmotionStroopDemo.mp4"),
        _ => ResolveAssetPath("Assets", "CaptureWorkbench", "Videos", "EyeCalibrationDemo.mp4")
    };

    public Uri DemoVideoUri => new(DemoVideoPath);

    public string CurrentModuleCode => ModuleProgressItems.Count == 0
        ? EyeCalibrationModuleCode
        : ModuleProgressItems[currentModuleIndex].Code;

    public int CurrentModuleTypeId => ModuleProgressItems.Count == 0
        ? AssessmentModuleTypeIds.EyeCalibration
        : ModuleProgressItems[currentModuleIndex].ModuleTypeId;

    private int CurrentModuleSequence => ModuleProgressItems.Count == 0
        ? 0
        : ModuleProgressItems[currentModuleIndex].Sequence;

    public string CurrentModule => ModuleProgressItems.Count == 0
        ? T("ModuleEyeCalibration")
        : ModuleProgressItems[currentModuleIndex].Name;

    public string NextModule => currentModuleIndex + 1 < ModuleProgressItems.Count
        ? ModuleProgressItems[currentModuleIndex + 1].Name
        : T("CaptureWorkspaceEnd");

    public string PrimaryActionText => isDemoCompleted
        ? T("CaptureWorkspaceReplayDemo")
        : T("CaptureWorkspacePlayDemo");

    public string SkipDemoButtonText => T("CaptureWorkspaceSkipDemo");

    public string SecondaryActionText => T("CaptureWorkspaceStart");

    public string WorkspaceTitleText => T("AssessmentCapture");

    public bool HasMatchedFollowUp => matchedFollowUpId.HasValue && patientService.CurrentPatient is not null;

    public string MatchedFollowUpText => matchedFollowUpId is long id
        ? string.Format(localization.Text("AssessmentEntryMatchedFollowUp"), id)
        : string.Empty;

    public void SetMatchedFollowUp(long detailId)
    {
        matchedFollowUpId = detailId;
        OnPropertyChanged(nameof(HasMatchedFollowUp));
        OnPropertyChanged(nameof(MatchedFollowUpText));
    }

    private void ClearMatchedFollowUpForPatientChange()
    {
        // 匹配 ID 只对完成匹配的当前患者有效，切换本地患者后不能继续显示。
        if (matchedFollowUpId is null)
        {
            return;
        }

        matchedFollowUpId = null;
        OnPropertyChanged(nameof(HasMatchedFollowUp));
        OnPropertyChanged(nameof(MatchedFollowUpText));
    }

    public string CurrentModuleBadgeText => T("CaptureWorkspaceModuleBadge", CurrentModule);

    public string WorkbenchStatusText => IsFormModule
        ? GetCurrentFormSaveStatusText()
        : FrameSaveStatusText;

    public string ProcessTitleText => T("CaptureWorkspaceProcessTitle");

    public string CurrentStepText => T("CaptureWorkspaceCurrentStep", CurrentDevStepText);

    public string DemoStepTitleText => T("CaptureWorkspaceDemoStep");

    public string FaceStepTitleText => T("CaptureWorkspaceFaceStep");

    public string CompletedStepTitleText => T("CaptureWorkspaceCompletedStep");

    public string FormFillStepTitleText => T("CaptureWorkspaceFormFillStep");

    public string FormCompletedStepTitleText => T("CaptureWorkspaceFormCompletedStep");

    public string SharedDisplayTitle => T("CaptureWorkspaceSharedDisplayTitle", CurrentModule);

    public string EnterFaceCheckButtonText => T("CaptureWorkspaceEnterFaceCheck");

    public string CameraPanelTitleText => T("CaptureWorkspaceCameraPanelTitle");

    public string RefreshButtonText => T("CaptureWorkspaceRefresh");

    public string CameraPreviewPlaceholderText => T("CaptureWorkspaceCameraPreview");

    public string ModuleFlowTitleText => T("CaptureWorkspaceModuleFlowTitle");

    public ICommand DevNextStepCommand { get; }

    public ICommand GoNextModuleCommand { get; }

    public ICommand RetryFailedModuleCommand { get; }

    public ICommand SwitchModuleCommand { get; }

    public ICommand RefreshCameraDevicesCommand { get; }

    public ICommand SubmitBasicInfoCommand { get; }

    public ICommand OpenBasicInfoOptionCommand { get; }

    public ICommand SelectBasicInfoOptionCommand { get; }

    public ICommand CloseBasicInfoOptionCommand { get; }

    public ICommand OpenQuestionnaireOptionCommand { get; }

    public ICommand SelectQuestionnaireOptionCommand { get; }

    public ICommand CloseQuestionnaireOptionCommand { get; }

    public ICommand PreviousQuestionnaireQuestionCommand { get; }

    public ICommand NextQuestionnaireQuestionCommand { get; }

    public ICommand SubmitQuestionnaireCommand { get; }

    public ObservableCollection<string> CameraDevices { get; } = [];

    public ObservableCollection<string> BasicInfoGenderItems { get; } = new(BasicInfoGenderOptions);

    public ObservableCollection<string> BasicInfoEducationItems { get; } = new(BasicInfoEducationOptions);

    public ObservableCollection<string> BasicInfoOccupationItems { get; } = new(BasicInfoOccupationOptions);

    public ObservableCollection<string> BasicInfoIncomeItems { get; } = new(BasicInfoIncomeOptions);

    public ObservableCollection<string> CurrentBasicInfoOptions { get; } = [];

    public ObservableCollection<QuestionnaireQuestionItem> QuestionnaireQuestionItems => questionnaireSession.Questions;

    public ObservableCollection<string> CurrentQuestionnaireOptions { get; } = [];

    public ObservableCollection<ModuleProgressItem> ModuleProgressItems { get; } = [];

    public string SelectedCameraDevice
    {
        get => selectedCameraDevice;
        set
        {
            value ??= T("CaptureWorkspaceNoCameraSelected");
            if (SetProperty(ref selectedCameraDevice, value))
            {
                CameraStatusText = IsUnavailableCameraValue(value)
                    ? T("CaptureWorkspaceNoCameraAvailable")
                    : T("CaptureWorkspaceCameraSelected", value);
            }
        }
    }

    public string CameraStatusText
    {
        get => cameraStatusText;
        private set => SetProperty(ref cameraStatusText, value);
    }

    public string CurrentDevStepText => currentStep switch
    {
        CaptureWorkbenchStep.ModuleExecution when IsFormModule => T("CaptureWorkspaceFormFillStep"),
        CaptureWorkbenchStep.Saving => "3. 数据保存中",
        CaptureWorkbenchStep.Completed when IsFormModule => T("CaptureWorkspaceFormCompletedStep"),
        CaptureWorkbenchStep.Demo => T("CaptureWorkspaceDemoStep"),
        CaptureWorkbenchStep.FaceCheck => T("CaptureWorkspaceFaceStep"),
        CaptureWorkbenchStep.ModuleExecution => $"3. {CurrentModule}",
        CaptureWorkbenchStep.Completed => T("CaptureWorkspaceCompletedStep"),
        _ => T("CaptureWorkspacePrepareCheck")
    };

    public bool IsPrepareStep => currentStep == CaptureWorkbenchStep.Prepare;

    public bool IsDemoStep => currentStep == CaptureWorkbenchStep.Demo;

    public bool IsFaceStep => currentStep == CaptureWorkbenchStep.FaceCheck;

    public bool IsCalibrationStep => currentStep == CaptureWorkbenchStep.ModuleExecution;

    public bool IsImageBrowseStep => currentStep == CaptureWorkbenchStep.Completed;

    public bool IsDemoStage => currentStep == CaptureWorkbenchStep.Demo;

    /// <summary>V3 正式模块统一使用指导语页，不再播放演示视频。</summary>
    public bool IsInstructionStage => IsDemoStage && !IsFormModule && !IsSyncTestModule;

    public bool IsDemoMediaStage => IsDemoStage && !IsInstructionStage;

    public string InstructionText => localization.IsChinese ? "指导语" : "Instructions";

    public bool IsDemoPlaying => isDemoPlaying;

    public bool IsDemoCompleted => isDemoCompleted;

    public bool IsCalibrationStage => currentStep == CaptureWorkbenchStep.ModuleExecution;

    /// <summary>
    /// 当前是否处于模块正式采集阶段。
    /// 离开采集工作台前会读取该状态，避免用户误切页面导致本次未完成录制被丢弃。
    /// </summary>
    public bool IsExecutingCaptureTask => currentStep == CaptureWorkbenchStep.ModuleExecution && !IsFormModule;

    public bool IsQuestionnaireInProgress =>
        IsFormModule && currentStep == CaptureWorkbenchStep.ModuleExecution;

    public bool IsActiveForSessionSecurity => ShouldConfirmLeavingWorkbench
        || IsSavingStage
        || captureMediaService.IsCapturing
        || activeModuleAttempt is not null;

    public bool ShouldConfirmLeavingWorkbench => IsDemoPlaying
        || IsExecutingCaptureTask
        || IsQuestionnaireInProgress
        || IsSavingStage;

    public string CaptureLeaveWarningMessage => T("CaptureWorkspaceLeaveCaptureWarning", CurrentModule);

    public string QuestionnaireLeaveWarningTitle => localization.IsChinese ? "离开当前问卷" : "Leave questionnaire";

    public string QuestionnaireLeaveWarningMessage => localization.IsChinese
        ? $"当前正在填写 {CurrentModule}。如果现在离开，本次未提交的选项不会保存；再次进入需要从第 1 题重新填写。"
        : $"You are filling in {CurrentModule}. If you leave now, unsubmitted answers will not be saved. You will need to restart from Question 1.";

    public string WorkbenchLeaveWarningTitle => IsQuestionnaireInProgress
        ? QuestionnaireLeaveWarningTitle
        : IsDemoPlaying
            ? "中断演示播放"
            : "暂停采集任务";

    public string WorkbenchLeaveWarningMessage => IsQuestionnaireInProgress
        ? QuestionnaireLeaveWarningMessage
        : IsDemoPlaying
            ? T("CaptureWorkspaceLeaveDemoWarning", CurrentModule)
            : CaptureLeaveWarningMessage;

    public string WorkbenchLeaveConfirmText => localization.IsChinese ? "确认离开" : "Leave";

    public string WorkbenchLeaveCancelText => IsQuestionnaireInProgress
        ? localization.IsChinese ? "继续填写" : "Continue"
        : IsDemoPlaying
            ? localization.IsChinese ? "继续观看" : "Continue watching"
            : localization.IsChinese ? "继续采集" : "Continue capture";

    public bool IsEyeCalibrationModule => CurrentModuleCode == EyeCalibrationModuleCode;

    public bool IsPictureBrowseModule => CurrentModuleCode == PictureBrowseModuleCode;

    public bool IsVideoBrowseModule => CurrentModuleCode == VideoBrowseModuleCode;

    public bool IsVoiceBaselineModule => CurrentModuleCode == VoiceBaselineModuleCode;

    public bool IsWordReadingModule => CurrentModuleCode == WordReadingModuleCode;

    public bool IsShortTextReadingModule => CurrentModuleCode == ShortTextReadingModuleCode;

    public bool IsEmotionQuestionModule => CurrentModuleCode == EmotionQuestionModuleCode;

    public bool IsDotProbeModule => CurrentModuleCode == DotProbeModuleCode;

    public bool IsEmotionOddballModule => CurrentModuleCode == EmotionOddballModuleCode;

    public bool IsEmotionLetterSearchModule => CurrentModuleCode == EmotionLetterSearchModuleCode;

    public bool IsEmotionStroopModule => CurrentModuleCode == EmotionStroopModuleCode;

    public bool IsBasicInfoModule => CurrentModuleCode == BasicInfoModuleCode;

    public bool IsQuestionnaireModule => GetQuestionnaireDefinition(CurrentModuleCode) is not null;

    public bool IsFormModule => IsFormModuleCode(CurrentModuleCode);

    public bool IsCaptureTaskModule => !IsFormModule;

    public bool IsSyncTestModule => CurrentModuleCode == SyncTestModuleCode;

    public bool IsEyeCalibrationStage => IsCalibrationStage && IsEyeCalibrationModule;

    public bool IsPictureBrowseStage => IsCalibrationStage && IsPictureBrowseModule;

    public bool IsVideoBrowseStage => IsCalibrationStage && IsVideoBrowseModule;

    public bool IsVoiceBaselineStage => IsCalibrationStage && IsVoiceBaselineModule;

    public bool IsWordReadingStage => IsCalibrationStage && IsWordReadingModule;

    public bool IsShortTextReadingStage => IsCalibrationStage && IsShortTextReadingModule;

    public bool IsEmotionQuestionStage => IsCalibrationStage && IsEmotionQuestionModule;

    public bool IsDotProbeStage => IsCalibrationStage && IsDotProbeModule;

    public bool IsEmotionOddballStage => IsCalibrationStage && IsEmotionOddballModule;

    public bool IsEmotionLetterSearchStage => IsCalibrationStage && IsEmotionLetterSearchModule;

    public bool IsEmotionStroopStage => IsCalibrationStage && IsEmotionStroopModule;

    public bool IsBasicInfoStage => IsCalibrationStage && IsBasicInfoModule;

    public bool IsQuestionnaireStage => IsCalibrationStage && IsQuestionnaireModule;

    public bool IsSyncTestStage => IsCalibrationStage && IsSyncTestModule;

    public bool IsPictureShowing => IsPictureBrowseStage && pictureBrowsePhase == PictureBrowsePhase.ShowingImage;

    public bool IsPictureFixation => IsPictureBrowseStage && pictureBrowsePhase == PictureBrowsePhase.Fixation;

    public bool IsPictureBlank => IsPictureBrowseStage
        && pictureBrowsePhase is PictureBrowsePhase.Blank or PictureBrowsePhase.FinalBlank;

    public bool IsPictureResting => IsPictureBrowseStage && pictureBrowsePhase == PictureBrowsePhase.Resting;

    public bool ShowPictureStatusBadge => IsPictureShowing;

    public bool IsVideoBrowseBlank => IsVideoBrowseStage && videoBrowsePhase == VideoBrowsePhase.Blank;

    public bool IsVideoBrowsePlaying => IsVideoBrowseStage && videoBrowsePhase == VideoBrowsePhase.PlayingVideo;

    public bool IsVideoBrowseResting => IsVideoBrowseStage && videoBrowsePhase == VideoBrowsePhase.Resting;

    public bool ShowVideoStatusBadge => IsVideoBrowsePlaying;

    public bool IsVoiceBaselineWaiting => IsVoiceBaselineStage && voiceBaselinePhase == VoiceBaselinePhase.WaitingToStart;

    public bool IsVoiceBaselinePreparing => IsVoiceBaselineStage && voiceBaselinePhase == VoiceBaselinePhase.Preparing;

    public bool IsVoiceBaselineRecording => IsVoiceBaselineStage && voiceBaselinePhase == VoiceBaselinePhase.Recording;

    public bool IsVoiceBaselineResting => IsVoiceBaselineStage && voiceBaselinePhase == VoiceBaselinePhase.Resting;

    public bool IsVoiceBaselinePromptVisible => IsVoiceBaselineStage;

    public bool ShowVoiceBaselineStartAction => IsVoiceBaselineWaiting
        && voiceBaselineIndex < VoiceBaselineItems.Length
        && !voiceBaselineMediaFinalizing;

    public bool CanFinishVoiceBaselineSegment => IsVoiceBaselineRecording
        && voiceBaselineRemainingSeconds <= VoiceBaselineMaximumSegmentSeconds - VoiceBaselineMinimumSegmentSeconds
        && !voiceBaselineMediaFinalizing;

    public string VoiceBaselineFinishButtonText => T("CaptureWorkspaceVoiceBaselineFinish");

    public bool IsWordReadingWaiting => IsWordReadingStage && wordReadingPhase == WordReadingPhase.WaitingToStart;

    public bool IsWordReadingActive => IsWordReadingStage && wordReadingPhase == WordReadingPhase.Reading;

    public bool IsWordReadingResting => IsWordReadingStage && wordReadingPhase == WordReadingPhase.Resting;

    public bool IsWordReadingPromptVisible => IsWordReadingStage && wordReadingPhase != WordReadingPhase.Resting;

    public bool ShowWordReadingStartAction => IsWordReadingWaiting && wordReadingIndex == 0;

    public bool IsFallbackStage => !IsDemoStage && !IsEyeCalibrationStage && !IsPictureBrowseStage && !IsVideoBrowseStage && !IsVoiceBaselineStage && !IsWordReadingStage && !IsShortTextReadingStage && !IsEmotionQuestionStage && !IsDotProbeStage && !IsEmotionOddballStage && !IsEmotionLetterSearchStage && !IsEmotionStroopStage && !IsBasicInfoStage && !IsQuestionnaireStage && !IsSyncTestStage && !IsSavingStage;

    public bool IsCompletionStage => currentStep == CaptureWorkbenchStep.Completed;

    public bool IsSavingStage => currentStep == CaptureWorkbenchStep.Saving;

    public bool IsModuleSaveFailed => isModuleSaveFailed;

    public bool IsModuleSavingInProgress => IsSavingStage && !isModuleSaveFailed;

    public string SavingStageTitleText => isModuleSaveFailed
        ? "数据保存失败"
        : "任务完成，数据保存中";

    public string SavingStageDescriptionText => isModuleSaveFailed
        ? "本模块结果未生效，不能进入下一模块。请从本模块第 1 步重新完成。"
        : "正在整理音视频并写入本次评估记录，请勿切换患者或关闭软件。";

    public bool IsGenericFallbackStage => IsFallbackStage && !IsCompletionStage && !IsFaceStep;

    public bool ShowDemoPlayAction => IsDemoStep
        && !IsInstructionStage
        && !isDemoPlaying
        && !isDemoCompleted;

    public bool ShowDevelopmentSkipDemoAction =>
        IsDevelopmentModuleNavigationEnabled
        && IsDemoStep
        && isDemoPlaying
        && activeModuleAttempt is null
        && !captureMediaService.IsCapturing;

    public bool ShowFaceCheckAction => IsDemoStep
        && !IsFormModule
        && (isDemoCompleted || IsInstructionStage);

    public string FaceReadinessTitleText => T("CaptureWorkspaceFaceReadinessTitle");

    public bool IsFaceReady => faceReadinessState == FaceReadinessState.Ready;

    public double FaceReadinessProgressPercent => faceReadinessProgressPercent;

    public string FaceReadinessBadgeText => faceReadinessState switch
    {
        FaceReadinessState.Stabilizing => T("CaptureWorkspaceFaceReadinessBadgeStabilizing"),
        FaceReadinessState.Ready => T("CaptureWorkspaceFaceReadinessBadgeReady"),
        _ => T("CaptureWorkspaceFaceReadinessBadgeWaiting")
    };

    public string FaceReadinessStatusText => faceReadinessState switch
    {
        FaceReadinessState.Stabilizing => T(
            "CaptureWorkspaceFaceReadinessStabilizing",
            faceReadinessRemainingSeconds),
        FaceReadinessState.Ready => T("CaptureWorkspaceFaceReadinessReady"),
        _ => string.IsNullOrWhiteSpace(faceReadinessReasonText)
            ? T("CaptureWorkspaceFaceReadinessWaiting")
            : faceReadinessReasonText
    };

    public Brush FaceReadinessAccentBrush => faceReadinessState switch
    {
        FaceReadinessState.Stabilizing => FaceStabilizingBrush,
        FaceReadinessState.Ready => FaceReadyBrush,
        _ => FaceNotReadyBrush
    };

    public bool ShowSyncTestStartAction => IsSyncTestStage && !isSyncTestRunning && syncTestRemainingSeconds == SyncTestDurationSeconds;

    public bool ShowSyncTestRunning => IsSyncTestStage && isSyncTestRunning;

    public bool IsSyncTestRecordingActive => IsSyncTestStage && isSyncTestRunning;

    public bool CanStartCalibration => isDemoCompleted
        && (currentStep == CaptureWorkbenchStep.Demo
            || currentStep == CaptureWorkbenchStep.FaceCheck && IsFaceReady);

    // Short-text pages are intentionally distraction-free; their legacy bottom
    // notice must never be rendered even when a previous stage left text behind.
    public bool HasStageNotice => !IsShortTextReadingStage && !string.IsNullOrWhiteSpace(stageNoticeText);

    public bool HasSelectedCamera => !IsUnavailableCameraValue(SelectedCameraDevice);

    public string PlaybackTimeText
    {
        get => playbackTimeText;
        private set => SetProperty(ref playbackTimeText, value);
    }

    public string CalibrationText
    {
        get => calibrationText;
        private set => SetProperty(ref calibrationText, value);
    }

    public double CalibrationX
    {
        get => calibrationX;
        private set
        {
            if (SetProperty(ref calibrationX, value))
            {
                OnPropertyChanged(nameof(CalibrationCanvasLeft));
            }
        }
    }

    public double CalibrationY
    {
        get => calibrationY;
        private set
        {
            if (SetProperty(ref calibrationY, value))
            {
                OnPropertyChanged(nameof(CalibrationCanvasTop));
            }
        }
    }

    public double CalibrationCanvasLeft => (CalibrationX / 100d * 760d) - 28d;

    public double CalibrationCanvasTop => (CalibrationY / 100d * 460d) - 28d;

    public string CalibrationMarkerColor
    {
        get => calibrationMarkerColor;
        private set => SetProperty(ref calibrationMarkerColor, value);
    }

    public bool IsCalibrationMarkerVisible
    {
        get => isCalibrationMarkerVisible;
        private set => SetProperty(ref isCalibrationMarkerVisible, value);
    }

    public int CalibrationMoveDurationMilliseconds
    {
        get => calibrationMoveDurationMilliseconds;
        private set => SetProperty(ref calibrationMoveDurationMilliseconds, value);
    }

    public int CalibrationAnimationSequence
    {
        get => calibrationAnimationSequence;
        private set => SetProperty(ref calibrationAnimationSequence, value);
    }

    public string CalibrationTrialTitle => T("CaptureWorkspaceCalibrationTrialTitle", calibrationTrialIndex);

    public string FrameSaveStatusText
    {
        get => frameSaveStatusText;
        private set => SetProperty(ref frameSaveStatusText, value);
    }

    public string FrameOutputDirectory
    {
        get => frameOutputDirectory;
        private set => SetProperty(ref frameOutputDirectory, value);
    }

    public string StageNoticeText
    {
        get => stageNoticeText;
        private set
        {
            if (SetProperty(ref stageNoticeText, value))
            {
                OnPropertyChanged(nameof(HasStageNotice));
            }
        }
    }

    public string PictureBrowseImagePath
    {
        get => pictureBrowseImagePath;
        private set => SetProperty(ref pictureBrowseImagePath, value);
    }

    public string PictureBrowseStatusText
    {
        get => pictureBrowseStatusText;
        private set => SetProperty(ref pictureBrowseStatusText, value);
    }

    public string PictureBrowseRestText
    {
        get => pictureBrowseRestText;
        private set => SetProperty(ref pictureBrowseRestText, value);
    }

    public int? CurrentPictureBrowseImageType
    {
        get => currentPictureBrowseImageType;
        private set => SetProperty(ref currentPictureBrowseImageType, value);
    }

    public string VideoBrowseVideoPath
    {
        get => videoBrowseVideoPath;
        private set
        {
            if (SetProperty(ref videoBrowseVideoPath, value))
            {
                OnPropertyChanged(nameof(VideoBrowseVideoUri));
            }
        }
    }

    public Uri? VideoBrowseVideoUri => string.IsNullOrWhiteSpace(VideoBrowseVideoPath)
        ? null
        : new Uri(VideoBrowseVideoPath);

    public string VideoBrowseStatusText
    {
        get => videoBrowseStatusText;
        private set => SetProperty(ref videoBrowseStatusText, value);
    }

    public string VideoBrowseRestText
    {
        get => videoBrowseRestText;
        private set => SetProperty(ref videoBrowseRestText, value);
    }

    public int? CurrentVideoBrowseVideoType
    {
        get => currentVideoBrowseVideoType;
        private set => SetProperty(ref currentVideoBrowseVideoType, value);
    }

    public string VoiceBaselinePromptText => voiceBaselineIndex >= 0 && voiceBaselineIndex < VoiceBaselineItems.Length
        ? VoiceBaselineItems[voiceBaselineIndex].PromptText
        : T("CaptureWorkspaceVoiceBaselineCompleted");

    public string VoiceBaselineSyllableText => voiceBaselinePhase switch
    {
        VoiceBaselinePhase.WaitingToStart => string.Empty,
        VoiceBaselinePhase.Preparing => T("CaptureWorkspaceVoiceBaselinePrepareDisplay"),
        VoiceBaselinePhase.Resting => T("CaptureWorkspaceVoiceBaselineRestCountdown", voiceBaselineRemainingSeconds),
        _ when voiceBaselineIndex >= 0 && voiceBaselineIndex < VoiceBaselineItems.Length
            => VoiceBaselineItems[voiceBaselineIndex].PromptText,
        _ => string.Empty
    };

    public string VoiceBaselineTitleText => T("CaptureWorkspaceVoiceBaseline");

    public string VoiceBaselineStartButtonText => T("CaptureWorkspaceVoiceBaselineStart");

    public string WordReadingTitleText => T("CaptureWorkspaceWordReading");

    public string WordReadingStartButtonText => T("CaptureWorkspaceWordReadingStart");

    public string WordReadingGroupTitleText => T("CaptureWorkspaceWordReadingGroup", wordReadingIndex + 1, WordReadingGroups.Length);

    public string[] WordReadingCurrentWords => wordReadingIndex >= 0 && wordReadingIndex < WordReadingGroups.Length
        ? WordReadingGroups[wordReadingIndex].Words
        : [];

    public string SyncTestTitleText => T("ModuleSyncTest");

    public string SyncTestStartButtonText => T("CaptureWorkspaceSyncTestStart");

    public string RestTitleText => T("CaptureWorkspaceRestTitle");

    public string VoiceBaselineStatusText
    {
        get => voiceBaselineStatusText;
        private set => SetProperty(ref voiceBaselineStatusText, value);
    }

    public string VoiceBaselineRestText
    {
        get => voiceBaselineRestText;
        private set => SetProperty(ref voiceBaselineRestText, value);
    }

    public string WordReadingStatusText
    {
        get => wordReadingStatusText;
        private set => SetProperty(ref wordReadingStatusText, value);
    }

    public string WordReadingRestText
    {
        get => wordReadingRestText;
        private set => SetProperty(ref wordReadingRestText, value);
    }

    public string SelectedBasicInfoGender
    {
        get => selectedBasicInfoGender;
        set => SetBasicInfoField(ref selectedBasicInfoGender, value);
    }

    public string SelectedBasicInfoGenderDisplay => ToOptionDisplay(SelectedBasicInfoGender);

    public string BasicInfoBirthDateText
    {
        get => basicInfoBirthDateText;
        set
        {
            if (SetBasicInfoField(ref basicInfoBirthDateText, value))
            {
                OnPropertyChanged(nameof(BasicInfoBirthDateDisplay));
            }
        }
    }

    public string BasicInfoBirthDateDisplay => ToOptionDisplay(BasicInfoBirthDateText);

    public string SelectedBasicInfoEducation
    {
        get => selectedBasicInfoEducation;
        set => SetBasicInfoField(ref selectedBasicInfoEducation, value);
    }

    public string SelectedBasicInfoEducationDisplay => ToOptionDisplay(SelectedBasicInfoEducation);

    public string SelectedBasicInfoOccupation
    {
        get => selectedBasicInfoOccupation;
        set => SetBasicInfoField(ref selectedBasicInfoOccupation, value);
    }

    public string SelectedBasicInfoOccupationDisplay => ToOptionDisplay(SelectedBasicInfoOccupation);

    public string SelectedBasicInfoIncomeLevel
    {
        get => selectedBasicInfoIncomeLevel;
        set => SetBasicInfoField(ref selectedBasicInfoIncomeLevel, value);
    }

    public string SelectedBasicInfoIncomeLevelDisplay => ToOptionDisplay(SelectedBasicInfoIncomeLevel);

    public bool IsBasicInfoOptionPanelOpen
    {
        get => isBasicInfoOptionPanelOpen;
        private set => SetProperty(ref isBasicInfoOptionPanelOpen, value);
    }

    public string BasicInfoOptionTitle
    {
        get => basicInfoOptionTitle;
        private set => SetProperty(ref basicInfoOptionTitle, value);
    }

    public string BasicInfoValidationMessage
    {
        get => basicInfoValidationMessage;
        private set
        {
            if (SetProperty(ref basicInfoValidationMessage, value))
            {
                OnPropertyChanged(nameof(HasBasicInfoValidationMessage));
            }
        }
    }

    public bool HasBasicInfoValidationMessage => !string.IsNullOrWhiteSpace(BasicInfoValidationMessage);

    public string BasicInfoFormTitleText => T("CaptureWorkspaceBasicInfoTitle");

    public string BasicInfoFormDescriptionText => T("CaptureWorkspaceBasicInfoDescription");

    public string BasicInfoSubmitButtonText => T("CaptureWorkspaceBasicInfoSubmit");

    public string BasicInfoCompletedText => T("CaptureWorkspaceBasicInfoCompleted", NextModule);

    public string BasicInfoGenderLabelText => T("CaptureWorkspaceBasicInfoGender");

    public string BasicInfoBirthDateLabelText => T("CaptureWorkspaceBasicInfoBirthDate");

    public string BasicInfoBirthDateHintText => T("CaptureWorkspaceBasicInfoBirthDateHint");

    public string BasicInfoEducationLabelText => T("CaptureWorkspaceBasicInfoEducationWithHint");

    public string BasicInfoOccupationLabelText => T("CaptureWorkspaceBasicInfoOccupation");

    public string BasicInfoIncomeLevelLabelText => T("CaptureWorkspaceBasicInfoIncomeLevel");

    public string BasicInfoEditOptionText => T("CaptureWorkspaceEditOption");

    public string BasicInfoChooseOneOptionText => T("CaptureWorkspaceChooseOneOption");

    public string CancelText => T("CaptureWorkspaceCancel");

    public string QuestionnaireTitleText => GetQuestionnaireDefinition(CurrentModuleCode) is { } definition
        ? T(definition.TitleKey)
        : CurrentModule;

    public string QuestionnaireSubtitleText => GetQuestionnaireDefinition(CurrentModuleCode) is { } definition
        ? T(definition.SubtitleKey)
        : string.Empty;

    public string QuestionnaireInstructionText => GetQuestionnaireDefinition(CurrentModuleCode) is { } definition
        ? T(definition.InstructionKey)
        : string.Empty;

    public string QuestionnaireSubmitButtonText => T("CaptureWorkspaceQuestionnaireSubmit", NextModule);

    public string QuestionnaireCompletedText => T("CaptureWorkspaceQuestionnaireCompleted", CurrentModule, NextModule);

    public QuestionnaireQuestionItem? CurrentQuestionnaireQuestion => questionnaireSession.Current;

    public int CurrentQuestionnaireQuestionNumber => QuestionnaireQuestionItems.Count == 0
        ? 0
        : questionnaireSession.CurrentNumber;

    public int QuestionnaireQuestionCount => QuestionnaireQuestionItems.Count;

    public string QuestionnaireProgressText => localization.IsChinese
        ? $"第 {CurrentQuestionnaireQuestionNumber} / {QuestionnaireQuestionCount} 题"
        : $"Question {CurrentQuestionnaireQuestionNumber} / {QuestionnaireQuestionCount}";

    public string QuestionnairePreviousButtonText => localization.IsChinese ? "上一题" : "Previous";

    public string QuestionnaireNextButtonText => localization.IsChinese ? "下一题" : "Next";

    public bool CanGoPreviousQuestionnaireQuestion => questionnaireSession.CanMovePrevious;

    public bool CanGoNextQuestionnaireQuestion => questionnaireSession.CanMoveNext;

    public bool ShowQuestionnaireNextButton => CanGoNextQuestionnaireQuestion;

    public bool ShowQuestionnaireSubmitButton => QuestionnaireQuestionItems.Count > 0 && !CanGoNextQuestionnaireQuestion;

    public bool IsQuestionnaireOptionPanelOpen
    {
        get => isQuestionnaireOptionPanelOpen;
        private set => SetProperty(ref isQuestionnaireOptionPanelOpen, value);
    }

    public string QuestionnaireOptionTitle
    {
        get => questionnaireOptionTitle;
        private set => SetProperty(ref questionnaireOptionTitle, value);
    }

    public string QuestionnaireValidationMessage
    {
        get => questionnaireValidationMessage;
        private set
        {
            if (SetProperty(ref questionnaireValidationMessage, value))
            {
                OnPropertyChanged(nameof(HasQuestionnaireValidationMessage));
            }
        }
    }

    public bool HasQuestionnaireValidationMessage => !string.IsNullOrWhiteSpace(QuestionnaireValidationMessage);

    public string SyncTestStatusText => isSyncTestRunning
        ? T("CaptureWorkspaceSyncTestRunning", syncTestRemainingSeconds)
        : T("CaptureWorkspaceSyncTestReady");

    public Brush PrepareStepBrush => StepBrush(0);

    public Brush DemoStepBrushValue => StepBrush(1);

    public Brush FaceStepBrush => StepBrush(2);

    public Brush CalibrationStepBrush => StepBrush(3);

    public Brush ImageBrowseStepBrush => StepBrush(4);

    public Brush FormFillStepBrush => FormStepBrush(CaptureWorkbenchStep.ModuleExecution);

    public Brush FormCompletedStepBrush => FormStepBrush(CaptureWorkbenchStep.Completed);

    public Brush PrepareStepTextBrush => StepTextBrush(0);

    public Brush DemoStepTextBrush => StepTextBrush(1);

    public Brush FaceStepTextBrush => StepTextBrush(2);

    public Brush CalibrationStepTextBrush => StepTextBrush(3);

    public Brush ImageBrowseStepTextBrush => StepTextBrush(4);

    public Brush FormFillStepTextBrush => FormStepTextBrush(CaptureWorkbenchStep.ModuleExecution);

    public Brush FormCompletedStepTextBrush => FormStepTextBrush(CaptureWorkbenchStep.Completed);

    public string DevMainStageText => currentStep switch
    {
        CaptureWorkbenchStep.Prepare => T("CaptureWorkspaceMainPrepare"),
        CaptureWorkbenchStep.Demo => T("CaptureWorkspaceMainDemo"),
        CaptureWorkbenchStep.FaceCheck => T("CaptureWorkspaceMainFaceCheck"),
        CaptureWorkbenchStep.ModuleExecution when IsSyncTestModule => T("CaptureWorkspaceMainSyncTest"),
        CaptureWorkbenchStep.ModuleExecution when IsBasicInfoModule => T("CaptureWorkspaceMainBasicInfo"),
        CaptureWorkbenchStep.ModuleExecution when IsVoiceBaselineModule => T("CaptureWorkspaceMainVoiceBaseline"),
        CaptureWorkbenchStep.ModuleExecution when IsWordReadingModule => T("CaptureWorkspaceMainWordReading"),
        CaptureWorkbenchStep.ModuleExecution when IsShortTextReadingModule => T("CaptureWorkspaceMainShortTextReading"),
        CaptureWorkbenchStep.ModuleExecution when IsEmotionQuestionModule => T("CaptureWorkspaceMainEmotionQuestion"),
        CaptureWorkbenchStep.ModuleExecution when IsDotProbeModule => T("CaptureWorkspaceMainDotProbe"),
        CaptureWorkbenchStep.ModuleExecution when IsEmotionOddballModule => T("CaptureWorkspaceMainEmotionOddball"),
        CaptureWorkbenchStep.ModuleExecution when IsEmotionLetterSearchModule => T("CaptureWorkspaceMainEmotionLetterSearch"),
        CaptureWorkbenchStep.ModuleExecution when IsEmotionStroopModule => T("CaptureWorkspaceMainEmotionStroop"),
        CaptureWorkbenchStep.ModuleExecution when IsPictureBrowseModule => T("CaptureWorkspaceMainPictureBrowse"),
        CaptureWorkbenchStep.ModuleExecution when IsVideoBrowseModule => T("CaptureWorkspaceMainVideoBrowse"),
        CaptureWorkbenchStep.ModuleExecution => T("CaptureWorkspaceMainEyeCalibration"),
        CaptureWorkbenchStep.Saving => SavingStageTitleText,
        CaptureWorkbenchStep.Completed => T("CaptureWorkspaceMainCompleted", NextModule),
        _ => T("CaptureWorkspaceMainDemo")
    };

    public string DevHintText => currentStep == CaptureWorkbenchStep.ModuleExecution
        ? T("CaptureWorkspaceDevHintExecution")
        : T("CaptureWorkspaceDevHintDefault");

    public string StartButtonStateText => currentStep switch
    {
        CaptureWorkbenchStep.Demo when isDemoCompleted => T("CaptureWorkspaceStartButtonToFace"),
        CaptureWorkbenchStep.FaceCheck when IsFaceReady => T("CaptureWorkspaceStartButtonAvailable"),
        CaptureWorkbenchStep.FaceCheck => T("CaptureWorkspaceStartButtonLocked"),
        _ => CanStartCalibration ? T("CaptureWorkspaceStartButtonAvailable") : T("CaptureWorkspaceStartButtonLocked")
    };

    public string CameraConfirmationMessage()
    {
        return HasSelectedCamera
            ? T("CaptureWorkspaceCameraConfirmation", SelectedCameraDevice)
            : T("CaptureWorkspaceCameraUnavailableConfirmation");
    }

    public void BeginDemoPlayback()
    {
        StopModuleExecutionTimers();
        MoveToStep(CaptureWorkbenchStep.Demo);
        isDemoPlaying = true;
        isDemoCompleted = false;
        StageNoticeText = string.Empty;
        PlaybackTimeText = "00:00 / 播放中";
        NotifyStageChanged();
    }

    public bool SkipDemoForDevelopment()
    {
        if (!ShowDevelopmentSkipDemoAction)
        {
            return false;
        }

        StopModuleExecutionTimers();
        isDemoPlaying = false;
        isDemoCompleted = true;
        MoveToStep(CaptureWorkbenchStep.FaceCheck);
        PlaybackTimeText = T("CaptureWorkspaceDemoSkipped");
        StageNoticeText = T("CaptureWorkspaceDemoSkippedNotice");
        NotifyStageChanged();
        return true;
    }

    public void CancelDemoPlaybackForNavigation()
    {
        if (!isDemoPlaying)
        {
            return;
        }

        isDemoPlaying = false;
        isDemoCompleted = false;
        MoveToStep(CaptureWorkbenchStep.Demo);
        PlaybackTimeText = "00:00 / 未播放";
        StageNoticeText = T("CaptureWorkspaceDemoInterruptedNotice");
        NotifyStageChanged();
    }

    public void ResetFrameSavingStatus()
    {
        cameraCaptureService.SetRecordingEnabled(false);
        savedFrameCount = 0;
        frameOutputDirectory = string.Empty;
        FrameSaveStatusText = T("CaptureWorkspaceRecordingPending");
        OnPropertyChanged(nameof(FrameOutputDirectory));
    }

    public void DiscardFrameSavingStatus()
    {
        cameraCaptureService.SetRecordingEnabled(false);
        savedFrameCount = 0;
        frameOutputDirectory = string.Empty;
        FrameSaveStatusText = T("CaptureWorkspaceRecordingDiscarded");
        OnPropertyChanged(nameof(FrameOutputDirectory));
    }

    public void DiscardCurrentModuleExecution(string message, bool notifyUser = false)
    {
        if (currentStep != CaptureWorkbenchStep.ModuleExecution)
        {
            return;
        }

        StopModuleExecutionTimers();
        faceConditionMonitor.Reset();
        DiscardFrameSavingStatus();
        if (captureMediaService.IsCapturing)
        {
            captureMediaService.RequestStop(CaptureMediaStopReason.Discarded, message);
        }

        MoveToStep(CaptureWorkbenchStep.FaceCheck);
        isDemoCompleted = true;
        isDemoPlaying = false;
        StageNoticeText = message;
        if (IsSyncTestModule)
        {
            MoveToStep(CaptureWorkbenchStep.ModuleExecution);
            syncTestRemainingSeconds = SyncTestDurationSeconds;
            isSyncTestRunning = false;
        }

        if (notifyUser)
        {
            toastService.ShowError(T("CaptureWorkspaceFaceAttemptInvalidTitle"), message);
        }

        NotifyStageChanged();
    }

    internal FaceConditionMonitorUpdate ObserveFaceCondition(
        CameraFaceState state,
        long timestamp)
    {
        if (!IsExecutingCaptureTask || !IsMediaRecording || IsSyncTestModule)
        {
            faceConditionMonitor.Reset();
            return new FaceConditionMonitorUpdate(state, TimeSpan.Zero, false);
        }

        // 图片浏览休息末段由专用取景检查暂停倒计时；休息期间不应因全局异常监视器直接作废整段录制。
        if (IsPictureResting)
        {
            faceConditionMonitor.Reset();
            return new FaceConditionMonitorUpdate(state, TimeSpan.Zero, false);
        }

        var update = faceConditionMonitor.Observe(state, timestamp);
        if (update.JustConfirmed)
        {
            var message = T(
                "CaptureWorkspaceFaceAttemptInvalid",
                FaceStateReasonText(state));
            DiscardCurrentModuleExecution(message, notifyUser: true);
        }

        return update;
    }

    internal void ResetFaceConditionMonitoring() => faceConditionMonitor.Reset();

    internal FaceReadinessUpdate ObserveFaceReadiness(
        CameraFaceState state,
        bool isPrimaryFaceInsideGuide,
        long timestamp)
    {
        if (!IsFaceStep)
        {
            ResetFaceReadiness();
            return new FaceReadinessUpdate(
                FaceReadinessState.NotReady,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1),
                0);
        }

        var meetsRequirements = state == CameraFaceState.Normal && isPrimaryFaceInsideGuide;
        var update = faceReadinessMonitor.Observe(meetsRequirements, timestamp);
        var reasonText = state == CameraFaceState.Normal && !isPrimaryFaceInsideGuide
            ? T("CaptureWorkspaceMoveFaceIntoFrame")
            : FaceStateReasonText(state);
        ApplyFaceReadinessUpdate(update, reasonText);
        return update;
    }

    internal void ResetFaceReadiness()
    {
        faceReadinessMonitor.Reset();
        ApplyFaceReadinessUpdate(
            new FaceReadinessUpdate(
                FaceReadinessState.NotReady,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(1),
                0),
            string.Empty);
    }

    private void ApplyFaceReadinessUpdate(FaceReadinessUpdate update, string reasonText)
    {
        var stateChanged = faceReadinessState != update.State;
        var progressChanged = Math.Abs(faceReadinessProgressPercent - update.ProgressPercent) > 0.01;
        var remainingSeconds = Math.Max(0, update.RemainingDuration.TotalSeconds);
        var remainingChanged = Math.Abs(faceReadinessRemainingSeconds - remainingSeconds) > 0.01;
        var reasonChanged = !string.Equals(faceReadinessReasonText, reasonText, StringComparison.Ordinal);
        if (!stateChanged && !progressChanged && !remainingChanged && !reasonChanged)
        {
            return;
        }

        faceReadinessState = update.State;
        faceReadinessProgressPercent = update.ProgressPercent;
        faceReadinessRemainingSeconds = remainingSeconds;
        faceReadinessReasonText = reasonText;
        OnPropertyChanged(nameof(IsFaceReady));
        OnPropertyChanged(nameof(FaceReadinessProgressPercent));
        OnPropertyChanged(nameof(FaceReadinessBadgeText));
        OnPropertyChanged(nameof(FaceReadinessStatusText));
        OnPropertyChanged(nameof(FaceReadinessAccentBrush));
        OnPropertyChanged(nameof(CanStartCalibration));
        OnPropertyChanged(nameof(StartButtonStateText));
    }

    private string FaceStateReasonText(CameraFaceState state) => state switch
    {
        CameraFaceState.Normal => T("CaptureWorkspaceFaceInsideFrame"),
        CameraFaceState.NoFace => T("CaptureWorkspaceNoFaceDetected"),
        CameraFaceState.MultipleFaces => T("CaptureWorkspaceMultipleFaces"),
        CameraFaceState.FaceOccluded => T("CaptureWorkspaceFaceOccluded"),
        CameraFaceState.EyesNotVisible => T("CaptureWorkspaceEyesNotVisible"),
        CameraFaceState.EyesClosed => T("CaptureWorkspaceEyesClosed"),
        CameraFaceState.MouthNotVisible => T("CaptureWorkspaceMouthNotVisible"),
        CameraFaceState.HeadPoseInvalid => T("CaptureWorkspaceHeadPoseInvalid"),
        _ => T("CaptureWorkspaceFaceDetectorUnavailable")
    };

    /// <summary>
    /// 演示视频播放完成后进入面部取景准备阶段。
    /// 正式采集前仍要求用户确认摄像头与人脸位置。
    /// </summary>
    public void CompleteDemo()
    {
        isDemoPlaying = false;
        isDemoCompleted = true;
        PlaybackTimeText = T("CaptureWorkspaceWatchDone");
        NotifyStageChanged();
    }

    /// <summary>
    /// 进入面部取景阶段。
    /// 该阶段只做开始前确认，不产生正式采集数据。
    /// </summary>
    public void BeginFaceCheck()
    {
        if (!isDemoCompleted && !IsInstructionStage)
        {
            return;
        }

        isDemoCompleted = true;
        MoveToStep(CaptureWorkbenchStep.FaceCheck);
        isDemoPlaying = false;
        StopModuleExecutionTimers();
        // Face readiness is communicated by the live preview/status controls;
        // do not render the legacy bottom instructional notice.
        StageNoticeText = string.Empty;
        NotifyStageChanged();
    }

    /// <summary>
    /// 开始当前模块第三步。
    /// 每个已实现任务模块进入自己的显式状态机，不允许通过通用兜底代替模块流程。
    /// </summary>
    public void StartCurrentModule()
    {
        if (!isDemoCompleted || currentStep != CaptureWorkbenchStep.FaceCheck)
        {
            return;
        }

        MoveToStep(CaptureWorkbenchStep.ModuleExecution);
        isDemoPlaying = false;
        StageNoticeText = string.Empty;
        if (IsPictureBrowseModule)
        {
            BeginPictureBrowseSequence();
        }
        else if (IsVideoBrowseModule)
        {
            BeginVideoBrowseSequence();
        }
        else if (IsVoiceBaselineModule)
        {
            BeginVoiceBaselineSequence();
        }
        else if (IsWordReadingModule)
        {
            BeginWordReadingSequence();
        }
        else if (IsShortTextReadingModule)
        {
            BeginShortTextReadingSequence();
        }
        else if (IsEmotionQuestionModule)
        {
            BeginEmotionQuestionSequence();
        }
        else if (IsDotProbeModule)
        {
            BeginDotProbeSequence();
        }
        else if (IsEmotionOddballModule)
        {
            BeginEmotionOddballSequence();
        }
        else if (IsEmotionLetterSearchModule)
        {
            BeginEmotionLetterSearchSequence();
        }
        else if (IsEmotionStroopModule)
        {
            BeginEmotionStroopSequence();
        }
        else
        {
            BeginCalibrationSequence();
        }

        NotifyStageChanged();
    }

    /// <summary>
    /// 正式视频自然播放结束后推进视频浏览流程。
    /// MediaElement 只能在 View 层监听结束事件，这里只接收“已结束”信号并更新业务状态。
    /// </summary>
    public void CompleteCurrentVideoBrowseVideo()
    {
        if (!IsVideoBrowsePlaying)
        {
            return;
        }

        var completedItem = videoBrowseIndex >= 0 && videoBrowseIndex < videoBrowseItems.Length
            ? videoBrowseItems[videoBrowseIndex]
            : null;
        var completedAt = DateTimeOffset.Now;
        var durationMs = currentVideoBrowseStartedAt.HasValue
            ? (long)(completedAt - currentVideoBrowseStartedAt.Value).TotalMilliseconds
            : 0L;
        RecordModuleEventSafely(
            "video_browse_video_completed",
            $"视频浏览第 {videoBrowseIndex + 1} 段播放完成",
            new
            {
                index = videoBrowseIndex + 1,
                total = videoBrowseItems.Length,
                videoType = completedItem?.VideoType ?? CurrentVideoBrowseVideoType,
                fileName = completedItem is null ? null : Path.GetFileName(completedItem.VideoPath),
                startedAtUnixMs = currentVideoBrowseStartedAt?.ToUnixTimeMilliseconds(),
                endedAtUnixMs = completedAt.ToUnixTimeMilliseconds(),
                durationMs,
                completedAtUnixMs = completedAt.ToUnixTimeMilliseconds()
            },
            currentVideoBrowseStartedAt,
            completedAt);

        videoBrowseIndex++;
        VideoBrowseVideoPath = string.Empty;
        CurrentVideoBrowseVideoType = null;
        currentVideoBrowseStartedAt = null;

        if (videoBrowseIndex >= videoBrowseItems.Length)
        {
            CompleteVideoBrowse();
            return;
        }

        videoBrowsePhase = VideoBrowsePhase.Resting;
        videoBrowseRestRemainingSeconds = CaptureWorkbenchForcedRestSeconds;
        UpdateVideoBrowseRestText();
        VideoBrowseStatusText = $"强制休息中：已完成 {videoBrowseIndex} / {videoBrowseItems.Length} 段";
        videoBrowseTimer.Interval = TimeSpan.FromSeconds(1);
        videoBrowseTimer.Start();
        NotifyStageChanged();
    }

    /// <summary>
    /// 开发专用音画同步测试。该模块跳过演示和面部取景，只做一段固定时长录制。
    /// </summary>
    public void StartSyncTest()
    {
        if (!IsSyncTestModule || currentStep != CaptureWorkbenchStep.ModuleExecution)
        {
            return;
        }

        StopModuleExecutionTimers();
        isDemoPlaying = false;
        isDemoCompleted = true;
        isSyncTestRunning = true;
        syncTestRemainingSeconds = SyncTestDurationSeconds;
        StageNoticeText = T("CaptureWorkspaceSyncTestActionNotice");
        syncTestTimer.Start();
        NotifyStageChanged();
    }

    /// <summary>
    /// 第一组词语由用户手动点击开始。
    /// 后续词组由休息倒计时结束后自动开始，不再出现手动按钮。
    /// </summary>
    public void StartWordReadingFirstGroup()
    {
        if (!IsWordReadingWaiting || wordReadingIndex != 0)
        {
            return;
        }

        StartWordReadingGroup();
    }

    public void ShowStageNotice(string message)
    {
        StageNoticeText = message;
        NotifyStageChanged();
    }

    public void BeginFrameSaving(string outputDirectory)
    {
        frameOutputDirectory = outputDirectory;
        savedFrameCount = 0;
        cameraCaptureService.SetRecordingEnabled(true);
        FrameSaveStatusText = T("CaptureWorkspaceRecordingActive");
        OnPropertyChanged(nameof(FrameOutputDirectory));
    }

    public void UpdateRecordedFrameCount(int frameCount)
    {
        if (frameCount <= savedFrameCount)
        {
            return;
        }

        savedFrameCount = frameCount;
        FrameSaveStatusText = T("CaptureWorkspaceRecordingFrameCount", savedFrameCount);
    }

    public void StopFrameSaving()
    {
        cameraCaptureService.SetRecordingEnabled(false);
        savedFrameCount = Math.Max(savedFrameCount, cameraCaptureService.RecordedFrameCount);
        if (savedFrameCount > 0)
        {
            FrameSaveStatusText = T("CaptureWorkspaceMergingFrameCount", savedFrameCount);
        }
    }

    public void CompleteMergedVideo()
    {
        FrameSaveStatusText = T("CaptureWorkspaceMergedFrameCount", savedFrameCount);
    }

    public void FailMergedVideo(string message)
    {
        FrameSaveStatusText = T("CaptureWorkspaceMergeFailed", message);
    }

    public void CompleteMergedVideoWithProbeError()
    {
        FrameSaveStatusText = T("CaptureWorkspaceMergedWithProbeError", savedFrameCount);
    }

    private void BeginModuleDataSaving()
    {
        if (currentStep == CaptureWorkbenchStep.Saving
            || (!IsDevelopmentModuleOverride && activeModuleAttempt is null))
        {
            return;
        }

        StopModuleExecutionTimers();
        currentStep = CaptureWorkbenchStep.Saving;
        isModuleSaveFailed = false;
        StopFrameSaving();
        if (activeModuleAttempt is { } attempt)
        {
            pendingLifecycleOperation = RunLifecycleOperationAsync(
                pendingLifecycleOperation,
                () => assessmentModuleLifecycle.MarkSavingAsync(attempt.AttemptId));
        }

        captureMediaService.RequestStop(
            CaptureMediaStopReason.Completed,
            $"模块 {CurrentModule} 已完成，开始保存音视频数据。");
        UpdateModuleProgressItems();
        NotifyStageChanged();
    }

    private void OnRecordingCompleted(object? sender, CaptureMediaCompleted args)
    {
        pendingLifecycleOperation = HandleRecordingCompletedAsync(pendingLifecycleOperation, args);
    }

    private void OnAudioLevelAvailable(object? sender, CaptureAudioLevel level)
    {
        if (!IsVoiceBaselineRecording
            || voiceBaselineHasVoice
            || voiceBaselineVoiceDetectionFinalized
            || voiceBaselineDetectionWindowStartedAt is not { } windowStart)
        {
            return;
        }

        var windowEnd = voiceBaselineDetectionWindowEndedAt ?? windowStart.AddSeconds(VoiceBaselineVoiceDetectionWindowSeconds);
        if (level.CapturedAt < windowStart || level.CapturedAt > windowEnd)
        {
            return;
        }

        if (level.Rms < VoiceBaselineVoicePresenceRmsThreshold
            && level.Peak < VoiceBaselineVoicePresenceRmsThreshold * 2)
        {
            return;
        }

        voiceBaselineHasVoice = true;
        voiceBaselineVoiceDetectedAt = level.CapturedAt;
        RecordModuleEventSafely(
            "voice_baseline_voice_detected",
            $"语音基线第 {voiceBaselineIndex + 1} 段检测到声音",
            new
            {
                segmentIndex = voiceBaselineIndex + 1,
                detectedAtUnixMs = level.CapturedAt.ToUnixTimeMilliseconds(),
                rms = level.Rms,
                peak = level.Peak,
                voiceDetectionWindowStartedAtUnixMs = windowStart.ToUnixTimeMilliseconds(),
                voiceDetectionWindowEndedAtUnixMs = windowEnd.ToUnixTimeMilliseconds(),
                voiceDetectionThreshold = VoiceBaselineVoicePresenceRmsThreshold
            },
            level.CapturedAt,
            level.CapturedAt);
        _ = RunOnUiThreadAsync(NotifyStageChanged);
    }

    private async Task<bool> HandleVoiceBaselineRecordingCompletedAsync(CaptureMediaCompleted args)
    {
        if (!IsVoiceBaselineModule
            || voiceBaselineActiveMediaSessionId != args.Session.SessionId)
        {
            return false;
        }

        voiceBaselineActiveMediaSessionId = null;
        voiceBaselineMediaFinalizing = false;
        if (args.Status is CaptureMediaCompletionStatus.Completed
            or CaptureMediaCompletionStatus.CompletedWithWarnings)
        {
            if (voiceBaselineIndex < VoiceBaselineItems.Length - 1)
            {
                voiceBaselineIndex++;
                currentVoiceBaselineStartedAt = null;
                voiceBaselinePhase = VoiceBaselinePhase.Resting;
                voiceBaselineRemainingSeconds = VoiceBaselineRestSeconds;
                VoiceBaselineStatusText = $"已完成 {voiceBaselineIndex} / {VoiceBaselineItems.Length} 段";
                UpdateVoiceBaselineRestText();
                var restStartedAt = DateTimeOffset.Now;
                RecordModuleEventSafely(
                    "voice_baseline_rest_started",
                    "语音基线两段之间休息开始",
                    new
                    {
                        completedSegmentCount = voiceBaselineIndex,
                        remainingSegmentCount = VoiceBaselineItems.Length - voiceBaselineIndex,
                        restSeconds = VoiceBaselineRestSeconds,
                        startedAtUnixMs = restStartedAt.ToUnixTimeMilliseconds()
                    },
                    restStartedAt,
                    null);
                voiceBaselineTimer.Start();
                await RunOnUiThreadAsync(NotifyStageChanged);
                return true;
            }

            var attempt = activeModuleAttempt;
            if (attempt is not null)
            {
                await assessmentModuleLifecycle.CompleteAsync(
                    attempt.AttemptId,
                    JsonSerializer.Serialize(new
                    {
                        args.Session.SessionId,
                        args.Session.OutputDirectory,
                        CompletionStatus = args.Status.ToString(),
                        args.ErrorCode,
                        args.Message
                    })).ConfigureAwait(false);
                await RunOnUiThreadAsync(() => ApplyRecordingCompletion(attempt, args));
            }
            else
            {
                await RunOnUiThreadAsync(() => ApplyDevelopmentRecordingCompletion(args));
            }

            return true;
        }

        var activeAttempt = activeModuleAttempt;
        if (activeAttempt is not null)
        {
            if (args.Status is CaptureMediaCompletionStatus.Discarded or CaptureMediaCompletionStatus.Interrupted)
            {
                await assessmentModuleLifecycle.CancelAsync(activeAttempt.AttemptId, args.Message ?? "采集过程被用户中断。").ConfigureAwait(false);
            }
            else
            {
                await assessmentModuleLifecycle.FailAsync(
                    activeAttempt.AttemptId,
                    args.ErrorCode ?? "MEDIA_CAPTURE_FAILED",
                    args.Message ?? "音视频保存失败。").ConfigureAwait(false);
            }

            await RunOnUiThreadAsync(() => ApplyRecordingCompletion(activeAttempt, args));
        }
        else
        {
            await RunOnUiThreadAsync(() => ApplyDevelopmentRecordingCompletion(args));
        }

        return true;
    }

    private async Task HandleRecordingCompletedAsync(Task previous, CaptureMediaCompleted args)
    {
        try
        {
            await previous.ConfigureAwait(false);
            if (await HandleShortTextReadingRecordingCompletedAsync(args).ConfigureAwait(false))
            {
                return;
            }

            if (await HandleVoiceBaselineRecordingCompletedAsync(args).ConfigureAwait(false))
            {
                return;
            }

            if (IsDevelopmentModuleOverride
                && args.Session.AssessmentAttemptId is null
                && activeDevelopmentMediaSessionId == args.Session.SessionId
                && string.Equals(
                    activeDevelopmentMediaModuleCode,
                    args.Session.ModuleCode,
                    StringComparison.Ordinal))
            {
                await RunOnUiThreadAsync(() => ApplyDevelopmentRecordingCompletion(args));
                return;
            }

            var attempt = activeModuleAttempt;
            if (attempt is null
                || attempt.AttemptId != args.Session.AssessmentAttemptId
                || !string.Equals(attempt.ModuleCode, args.Session.ModuleCode, StringComparison.Ordinal))
            {
                return;
            }

            if (args.Status is CaptureMediaCompletionStatus.Completed
                or CaptureMediaCompletionStatus.CompletedWithWarnings)
            {
                await assessmentModuleLifecycle.CompleteAsync(
                    attempt.AttemptId,
                    JsonSerializer.Serialize(new
                    {
                        args.Session.SessionId,
                        args.Session.OutputDirectory,
                        CompletionStatus = args.Status.ToString(),
                        args.ErrorCode,
                        args.Message
                    })).ConfigureAwait(false);
            }
            else if (args.Status is CaptureMediaCompletionStatus.Discarded
                or CaptureMediaCompletionStatus.Interrupted)
            {
                await assessmentModuleLifecycle.CancelAsync(
                    attempt.AttemptId,
                    args.Message ?? "采集过程被用户中断。").ConfigureAwait(false);
            }
            else
            {
                await assessmentModuleLifecycle.FailAsync(
                    attempt.AttemptId,
                    args.ErrorCode ?? "MEDIA_CAPTURE_FAILED",
                    args.Message ?? "音视频保存失败。").ConfigureAwait(false);
            }

            await RunOnUiThreadAsync(() => ApplyRecordingCompletion(attempt, args));
        }
        catch (Exception exception)
        {
            await RunOnUiThreadAsync(() =>
            {
                activeModuleAttempt = null;
                isModuleSaveFailed = true;
                currentStep = CaptureWorkbenchStep.Saving;
                FailMergedVideo(exception.Message);
                StageNoticeText = $"数据保存状态写入失败：{exception.Message}";
                UpdateModuleProgressItems();
                NotifyStageChanged();
            });
        }
    }

    private void ApplyDevelopmentRecordingCompletion(CaptureMediaCompleted args)
    {
        if (activeDevelopmentMediaSessionId != args.Session.SessionId)
        {
            return;
        }

        activeDevelopmentMediaSessionId = null;
        activeDevelopmentMediaModuleCode = null;
        if (args.Status is CaptureMediaCompletionStatus.Completed
            or CaptureMediaCompletionStatus.CompletedWithWarnings)
        {
            if (args.Status == CaptureMediaCompletionStatus.Completed)
            {
                CompleteMergedVideo();
            }
            else
            {
                CompleteMergedVideoWithProbeError();
            }

            isModuleSaveFailed = false;
            currentStep = CaptureWorkbenchStep.Completed;
            StageNoticeText = "采集已保存。";
        }
        else if (args.Status is CaptureMediaCompletionStatus.Discarded
            or CaptureMediaCompletionStatus.Interrupted)
        {
            isModuleSaveFailed = false;
            DiscardFrameSavingStatus();
            ReturnAfterRecordingInterrupted();
            isDemoPlaying = false;
            StageNoticeText = "开发调试采集已取消。";
        }
        else
        {
            isModuleSaveFailed = true;
            currentStep = CaptureWorkbenchStep.Saving;
            FailMergedVideo(args.Message ?? "请检查音视频采集环境");
            StageNoticeText = "音视频保存失败，请重试当前模块。";
        }

        UpdateModuleProgressItems();
        NotifyStageChanged();
    }

    private void ApplyRecordingCompletion(
        AssessmentModuleRunContext attempt,
        CaptureMediaCompleted args)
    {
        if (activeModuleAttempt?.AttemptId != attempt.AttemptId)
        {
            return;
        }

        activeModuleAttempt = null;
        if (args.Status is CaptureMediaCompletionStatus.Completed
            or CaptureMediaCompletionStatus.CompletedWithWarnings)
        {
            if (args.Status == CaptureMediaCompletionStatus.Completed)
            {
                CompleteMergedVideo();
            }
            else
            {
                CompleteMergedVideoWithProbeError();
            }

            isModuleSaveFailed = false;
            UpdateActiveRunAfterCurrentModuleCompletion();

            currentStep = CaptureWorkbenchStep.Completed;
            StageNoticeText = "数据保存完成，请手动进入下一模块。";
            toastService.ShowSuccess("数据保存完成", $"{attempt.ModuleName} 已保存，可以进入下一模块。");
        }
        else if (args.Status is CaptureMediaCompletionStatus.Discarded
            or CaptureMediaCompletionStatus.Interrupted)
        {
            isModuleSaveFailed = false;
            DiscardFrameSavingStatus();
            ReturnAfterRecordingInterrupted();
            isDemoPlaying = false;
            StageNoticeText = "本次尝试已取消，产生的数据不会参与正式结果统计。";
        }
        else
        {
            isModuleSaveFailed = true;
            currentStep = CaptureWorkbenchStep.Saving;
            FailMergedVideo(args.Message ?? "请检查音视频采集环境");
            StageNoticeText = "音视频自动重新保存仍失败；本模块未完成，不能进入下一模块。";
        }

        UpdateModuleProgressItems();
        NotifyStageChanged();
    }

    private void ReturnAfterRecordingInterrupted()
    {
        currentStep = IsSyncTestModule ? CaptureWorkbenchStep.ModuleExecution
            : IsEmotionQuestionModule || IsShortTextReadingModule ? CaptureWorkbenchStep.FaceCheck : CaptureWorkbenchStep.Demo;
        isDemoCompleted = IsSyncTestModule || IsEmotionQuestionModule || IsShortTextReadingModule;
        if (IsEmotionQuestionModule)
        {
            ResetEmotionQuestionState();
            ResetFaceReadiness();
        }
        else if (IsShortTextReadingModule)
        {
            ResetShortTextReadingState();
            ResetFaceReadiness();
        }
    }

    private static async Task RunOnUiThreadAsync(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        await dispatcher.InvokeAsync(action);
    }

    private static async Task RunLifecycleOperationAsync(Task previous, Func<Task> operation)
    {
        try
        {
            await previous.ConfigureAwait(false);
        }
        catch
        {
            // 上一个操作已自行呈现错误；后续取消仍需尝试落库。
        }

        await operation().ConfigureAwait(false);
    }

    public void UpdatePlaybackTime(TimeSpan position, TimeSpan? duration)
    {
        var current = FormatTime(position);
        var total = duration.HasValue ? FormatTime(duration.Value) : "播放中";
        PlaybackTimeText = $"{current} / {total}";
    }

    private void MoveToNextDevStep()
    {
        var nextStepIndex = currentDevStepIndex >= devSteps.Length - 1 ? 0 : currentDevStepIndex + 1;
        MoveToStep((CaptureWorkbenchStep)nextStepIndex);
        isDemoPlaying = false;
        if (currentStep is CaptureWorkbenchStep.FaceCheck or CaptureWorkbenchStep.ModuleExecution or CaptureWorkbenchStep.Completed)
        {
            isDemoCompleted = true;
        }

        if (currentStep == CaptureWorkbenchStep.ModuleExecution)
        {
            if (IsPictureBrowseModule)
            {
                BeginPictureBrowseSequence();
            }
            else if (IsVideoBrowseModule)
            {
                BeginVideoBrowseSequence();
            }
            else if (IsVoiceBaselineModule)
            {
                BeginVoiceBaselineSequence();
            }
            else if (IsWordReadingModule)
            {
                BeginWordReadingSequence();
            }
            else if (IsShortTextReadingModule)
            {
                BeginShortTextReadingSequence();
            }
            else if (IsEmotionQuestionModule)
            {
                BeginEmotionQuestionSequence();
            }
            else if (IsDotProbeModule)
            {
                BeginDotProbeSequence();
            }
            else if (IsEmotionOddballModule)
            {
                BeginEmotionOddballSequence();
            }
            else if (IsEmotionLetterSearchModule)
            {
                BeginEmotionLetterSearchSequence();
            }
            else if (IsEmotionStroopModule)
            {
                BeginEmotionStroopSequence();
            }
            else if (IsSyncTestModule)
            {
                syncTestRemainingSeconds = SyncTestDurationSeconds;
                isSyncTestRunning = false;
                StageNoticeText = T("CaptureWorkspaceSyncTestDevNotice");
            }
            else
            {
                BeginCalibrationSequence();
            }
        }
        else
        {
            StopModuleExecutionTimers();
        }

        NotifyStageChanged();
    }

    private async Task GoNextModuleAsync(CancellationToken cancellationToken = default)
    {
        await pendingLifecycleOperation.WaitAsync(cancellationToken);
        if (currentStep != CaptureWorkbenchStep.Completed
            || activeModuleAttempt is not null
            || currentModuleIndex + 1 >= ModuleProgressItems.Count
            || ModuleProgressItems[currentModuleIndex + 1].IsDevelopmentOnly)
        {
            return;
        }

        currentModuleIndex++;
        MoveToStep(IsFormModuleCode(CurrentModuleCode) ? CaptureWorkbenchStep.ModuleExecution : CaptureWorkbenchStep.Demo);
        isDemoCompleted = IsFormModule;
        isDemoPlaying = false;
        StopModuleExecutionTimers();
        PlaybackTimeText = "00:00 / 未播放";
        ResetFrameSavingStatus();
        ResetBasicInfoFormState(false);
        ResetQuestionnaireState(false);
        if (IsBasicInfoModule)
        {
            BeginBasicInfoForm();
        }
        else if (IsQuestionnaireModule)
        {
            BeginQuestionnaireForm();
        }
        StageNoticeText = string.Empty;
        UpdateModuleProgressItems();
        NotifyStageChanged();
        if (IsFormModule && isWorkbenchVisible)
        {
            await EnsureCurrentModuleAttemptStartedAsync(cancellationToken);
        }
    }

    private void ResetFailedModule()
    {
        if (!isModuleSaveFailed || activeModuleAttempt is not null)
        {
            return;
        }

        isModuleSaveFailed = false;
        StopModuleExecutionTimers();
        ResetFrameSavingStatus();
        MoveToStep(IsFormModule ? CaptureWorkbenchStep.ModuleExecution : CaptureWorkbenchStep.Demo);
        isDemoCompleted = IsFormModule;
        isDemoPlaying = false;
        StageNoticeText = "请重新完成当前模块。";
        UpdateModuleProgressItems();
        NotifyStageChanged();
    }

    /// <summary>
    /// 开发阶段允许点击右侧模块流程直接切换模块。
    /// 切换模块只重置当前模块内步骤，不清空整个采集工作台结果。
    /// </summary>
    private async Task SwitchModuleAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (!IsDevelopmentModuleNavigationEnabled || parameter is not ModuleProgressItem item)
        {
            return;
        }

        if (item.Index == currentModuleIndex
            && (activeModuleAttempt is not null || captureMediaService.IsCapturing))
        {
            return;
        }

        if (item.Index != currentModuleIndex)
        {
            if (activeModuleAttempt is not null
                && IsQuestionnaireInProgress
                && !ConfirmDiscardActiveQuestionnaire())
            {
                return;
            }

            if (captureMediaService.IsCapturing)
            {
                captureMediaService.RequestStop(
                    CaptureMediaStopReason.Discarded,
                    "开发调试切换模块，当前尝试已取消。");
                await captureMediaService.WaitForIdleAsync(cancellationToken);
            }
            else if (activeModuleAttempt is { } attempt)
            {
                await assessmentModuleLifecycle.CancelAsync(
                    attempt.AttemptId,
                    "开发调试切换模块，当前尝试已取消。",
                    cancellationToken);
                activeModuleAttempt = null;
            }
        }

        executionMode = AssessmentExecutionMode.DevelopmentDirect;
        activeDevelopmentMediaSessionId = null;
        activeDevelopmentMediaModuleCode = null;
        currentModuleIndex = item.Index;
        MoveToStep(item.Code == SyncTestModuleCode || IsFormModuleCode(item.Code) ? CaptureWorkbenchStep.ModuleExecution : CaptureWorkbenchStep.Demo);
        isDemoCompleted = item.Code == SyncTestModuleCode || IsFormModuleCode(item.Code);
        isDemoPlaying = false;
        StopModuleExecutionTimers();
        PlaybackTimeText = "00:00 / 未播放";
        ResetFrameSavingStatus();
        ResetBasicInfoFormState(false);
        ResetQuestionnaireState(false);
        StageNoticeText = item.Code == SyncTestModuleCode
            ? T("CaptureWorkspaceSyncTestDevSwitchNotice")
            : string.Empty;
        if (item.Code == BasicInfoModuleCode)
        {
            BeginBasicInfoForm();
        }
        else if (GetQuestionnaireDefinition(item.Code) is not null)
        {
            BeginQuestionnaireForm();
        }
        UpdateModuleProgressItems();
        NotifyStageChanged();
        OnPropertyChanged(nameof(CurrentModule));
        OnPropertyChanged(nameof(CurrentModuleCode));
        OnPropertyChanged(nameof(NextModule));
        OnPropertyChanged(nameof(SharedDisplayTitle));
        if (IsFormModule && isWorkbenchVisible && item.Index < FormalModuleCount)
        {
            await EnsureCurrentModuleAttemptStartedAsync(cancellationToken);
        }
    }

    public void DiscardActiveQuestionnaireAnswers()
    {
        ResetQuestionnaireState(clearAnswers: true);
    }

    private bool ConfirmDiscardActiveQuestionnaire()
    {
        var confirmed = userDialogService.ConfirmWarning(
            WorkbenchLeaveWarningTitle,
            WorkbenchLeaveWarningMessage,
            WorkbenchLeaveConfirmText,
            WorkbenchLeaveCancelText);

        if (confirmed)
        {
            DiscardActiveQuestionnaireAnswers();
        }

        return confirmed;
    }

    private void LoadModuleProgressItems()
    {
        LoadModuleProgressItems(CaptureWorkbenchModules.Select((module, sequence) =>
            (module, sequence)));
    }

    private void LoadFormalRunModuleProgressItems(IReadOnlyList<AssessmentRunModuleContext> moduleFlow)
    {
        var definitions = new List<(CaptureWorkbenchModule module, int sequence)>();
        foreach (var item in moduleFlow.OrderBy(static item => item.Sequence))
        {
            var module = CaptureWorkbenchModules.FirstOrDefault(candidate =>
                candidate.ModuleTypeId == item.ModuleTypeId);
            if (module is not null)
            {
                definitions.Add((module, item.Sequence));
            }
        }

        if (definitions.Count == 0)
        {
            throw new InvalidOperationException("本次评估流程中的模块在当前软件中均不可用。");
        }

        LoadModuleProgressItems(definitions);
    }

    private void LoadModuleProgressItems(IEnumerable<(CaptureWorkbenchModule module, int sequence)> definitions)
    {
        var orderedDefinitions = definitions.ToArray();
        workbenchCoordinator.Configure(orderedDefinitions.Select(item =>
            (item.module.Code, item.module.DisplayNameKey, item.module.IsDevelopmentOnly)));
        ModuleProgressItems.Clear();
        for (var i = 0; i < workbenchCoordinator.Modules.Count; i++)
        {
            var module = workbenchCoordinator.Modules[i];
            var definition = orderedDefinitions[i];
            ModuleProgressItems.Add(new ModuleProgressItem(
                i,
                definition.sequence,
                definition.module.ModuleTypeId,
                module.Code,
                module.DisplayNameKey,
                T(module.DisplayNameKey),
                module.IsDevelopmentOnly));
        }

        UpdateModuleProgressItems();
    }

    private void UpdateActiveRunAfterCurrentModuleCompletion()
    {
        if (activeRun is not { } run)
        {
            return;
        }

        var nextIndex = currentModuleIndex + 1;
        if (nextIndex < ModuleProgressItems.Count)
        {
            var next = ModuleProgressItems[nextIndex];
            activeRun = run with
            {
                NextModuleIndex = next.Sequence,
                NextModuleTypeId = next.ModuleTypeId
            };
            return;
        }

        activeRun = run with
        {
            NextModuleIndex = run.TotalModuleCount,
            NextModuleTypeId = null
        };
    }

    private void UpdateModuleProgressItems()
    {
        foreach (var item in ModuleProgressItems)
        {
            item.UpdateState(
                currentModuleIndex,
                currentStep == CaptureWorkbenchStep.Completed,
                currentStep == CaptureWorkbenchStep.Saving,
                isModuleSaveFailed,
                T("CaptureWorkspaceModuleCompleted"),
                T("CaptureWorkspaceModuleInProgress"),
                T("CaptureWorkspaceModulePending"));
        }
    }

    private void RefreshModuleDisplayNames()
    {
        foreach (var item in ModuleProgressItems)
        {
            item.UpdateName(T(item.DisplayNameKey));
        }

        foreach (var question in QuestionnaireQuestionItems)
        {
            question.UpdatePlaceholder(T("CaptureWorkspaceChooseOption"));
        }

        UpdateModuleProgressItems();
    }

    private void ResetVideoBrowseState()
    {
        videoBrowseTimer.Stop();
        videoBrowsePhase = VideoBrowsePhase.Idle;
        videoBrowseItems = [];
        videoBrowseIndex = 0;
        videoBrowseRestRemainingSeconds = 0;
        VideoBrowseVideoPath = string.Empty;
        CurrentVideoBrowseVideoType = null;
        currentVideoBrowseStartedAt = null;
        VideoBrowseStatusText = "待开始";
        VideoBrowseRestText = string.Empty;
    }

    /// <summary>
    /// 清理开发专用音画同步测试状态。
    /// </summary>
    private void ResetSyncTestState()
    {
        syncTestTimer.Stop();
        syncTestRemainingSeconds = SyncTestDurationSeconds;
        isSyncTestRunning = false;
    }

    private void ResetVoiceBaselineState()
    {
        voiceBaselineTimer.Stop();
        voiceBaselinePhase = VoiceBaselinePhase.Idle;
        voiceBaselineIndex = 0;
        voiceBaselineRemainingSeconds = VoiceBaselineMaximumSegmentSeconds;
        currentVoiceBaselineStartedAt = null;
        voiceBaselineDetectionWindowStartedAt = null;
        voiceBaselineDetectionWindowEndedAt = null;
        voiceBaselineVoiceDetectedAt = null;
        voiceBaselineHasVoice = false;
        voiceBaselineVoiceDetectionFinalized = false;
        voiceBaselineMediaFinalizing = false;
        voiceBaselineActiveMediaSessionId = null;
        VoiceBaselineStatusText = T("CaptureWorkspaceRecordingPending");
        VoiceBaselineRestText = string.Empty;
    }

    private void ResetWordReadingState()
    {
        wordReadingTimer.Stop();
        wordReadingPhase = WordReadingPhase.Idle;
        wordReadingIndex = 0;
        wordReadingRemainingSeconds = WordReadingGroupSeconds;
        currentWordReadingStartedAt = null;
        WordReadingStatusText = T("CaptureWorkspaceRecordingPending");
        WordReadingRestText = string.Empty;
    }

    /// <summary>
    /// 记录模块内部事件。
    /// 该方法只做辅助记录，失败不影响当前播放流程；正式错误会由服务层日志记录补充。
    /// </summary>
    private void RecordModuleEventSafely(
        string eventType,
        string message,
        object payload,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? endedAt = null)
    {
        moduleEventRecorder.Enqueue(eventType, message, payload, startedAt, endedAt);
    }

    public async Task FlushPendingModuleEventsAsync(CancellationToken cancellationToken = default)
    {
        await moduleEventRecorder.FlushAsync(cancellationToken);
        await pendingLifecycleOperation.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// 清理图片浏览内部状态。
    /// 用于切换模块、重播演示或重新开始当前模块。
    /// </summary>
    private void ResetPictureBrowseState()
    {
        pictureBrowseTimer.Stop();
        pictureBrowsePhase = PictureBrowsePhase.Idle;
        pictureBrowseItems = [];
        pictureBrowseVersion = string.Empty;
        pictureBrowseIndex = 0;
        pictureBrowseRestRemainingSeconds = 0;
        pictureBrowseRestPaused = false;
        pictureBrowseFixationStartedAt = null;
        pictureBrowseImageStartedAt = null;
        pictureBrowseRestStartedAt = null;
        pictureBrowseFinalBlankStartedAt = null;
        PictureBrowseImagePath = string.Empty;
        CurrentPictureBrowseImageType = null;
        PictureBrowseStatusText = "待开始";
        PictureBrowseRestText = string.Empty;
    }

    /// <summary>
    /// 停止当前模块第三步内部计时器。
    /// 注意：只停止流程计时，不处理音视频录制；录制由 ICaptureMediaService 管理。
    /// </summary>
    private void StopModuleExecutionTimers()
    {
        ResetCalibrationSequence();
        ResetPictureBrowseState();
        ResetVideoBrowseState();
        ResetVoiceBaselineState();
        ResetWordReadingState();
        ResetShortTextReadingState();
        ResetEmotionQuestionState();
        ResetDotProbeState();
        ResetEmotionOddballState();
        ResetEmotionLetterSearchState();
        ResetEmotionStroopState();
        ResetSyncTestState();
    }

    private void LoadCameraDevices()
    {
        var devices = DirectShowCameraEnumerator.GetVideoInputDeviceNames();
        CameraDevices.Clear();

        if (devices.Count == 0)
        {
            CameraDevices.Add(T("CaptureWorkspaceNoCameraDetected"));
        }
        else
        {
            foreach (var device in devices)
            {
                CameraDevices.Add(device);
            }
        }

        SelectedCameraDevice = CameraDevices[0];
    }

    private bool IsUnavailableCameraValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return string.Equals(value, T("CaptureWorkspaceNoCameraDetected"), StringComparison.Ordinal)
            || string.Equals(value, T("CaptureWorkspaceNoCameraSelected"), StringComparison.Ordinal)
            || value.StartsWith("未检测到", StringComparison.Ordinal)
            || value.StartsWith("未选择", StringComparison.Ordinal);
    }

    private void NotifyStageChanged()
    {
        // 采集工作台的界面状态由多个派生属性组合而成。
        // 任意模块或步骤变化后，统一刷新这些派生绑定，避免局部状态不同步。
        OnPropertyChanged(nameof(CurrentDevStepText));
        OnPropertyChanged(nameof(WorkspaceTitleText));
        OnPropertyChanged(nameof(MatchedFollowUpText));
        OnPropertyChanged(nameof(HasMatchedFollowUp));
        OnPropertyChanged(nameof(CurrentModuleBadgeText));
        OnPropertyChanged(nameof(WorkbenchStatusText));
        OnPropertyChanged(nameof(ProcessTitleText));
        OnPropertyChanged(nameof(CurrentStepText));
        OnPropertyChanged(nameof(DemoStepTitleText));
        OnPropertyChanged(nameof(FaceStepTitleText));
        OnPropertyChanged(nameof(CompletedStepTitleText));
        OnPropertyChanged(nameof(FormFillStepTitleText));
        OnPropertyChanged(nameof(FormCompletedStepTitleText));
        OnPropertyChanged(nameof(CurrentModule));
        OnPropertyChanged(nameof(CalibrationTrialTitle));
        OnPropertyChanged(nameof(CurrentModuleCode));
        OnPropertyChanged(nameof(NextModule));
        OnPropertyChanged(nameof(SharedDisplayTitle));
        OnPropertyChanged(nameof(EnterFaceCheckButtonText));
        OnPropertyChanged(nameof(CameraPanelTitleText));
        OnPropertyChanged(nameof(RefreshButtonText));
        OnPropertyChanged(nameof(CameraPreviewPlaceholderText));
        OnPropertyChanged(nameof(ModuleFlowTitleText));
        OnPropertyChanged(nameof(DemoVideoPath));
        OnPropertyChanged(nameof(DemoVideoUri));
        OnPropertyChanged(nameof(DevMainStageText));
        OnPropertyChanged(nameof(DevHintText));
        OnPropertyChanged(nameof(StartButtonStateText));
        OnPropertyChanged(nameof(PrimaryActionText));
        OnPropertyChanged(nameof(SkipDemoButtonText));
        OnPropertyChanged(nameof(IsDemoStage));
        OnPropertyChanged(nameof(IsInstructionStage));
        OnPropertyChanged(nameof(IsDemoMediaStage));
        OnPropertyChanged(nameof(InstructionText));
        OnPropertyChanged(nameof(IsDemoPlaying));
        OnPropertyChanged(nameof(IsDemoCompleted));
        OnPropertyChanged(nameof(IsCalibrationStage));
        OnPropertyChanged(nameof(IsExecutingCaptureTask));
        OnPropertyChanged(nameof(ShouldConfirmLeavingWorkbench));
        OnPropertyChanged(nameof(CaptureLeaveWarningMessage));
        OnPropertyChanged(nameof(WorkbenchLeaveWarningMessage));
        OnPropertyChanged(nameof(IsEyeCalibrationModule));
        OnPropertyChanged(nameof(IsPictureBrowseModule));
        OnPropertyChanged(nameof(IsVideoBrowseModule));
        OnPropertyChanged(nameof(IsVoiceBaselineModule));
        OnPropertyChanged(nameof(IsWordReadingModule));
        OnPropertyChanged(nameof(IsShortTextReadingModule));
        OnPropertyChanged(nameof(IsEmotionQuestionModule));
        OnPropertyChanged(nameof(IsDotProbeModule));
        OnPropertyChanged(nameof(IsEmotionOddballModule));
        OnPropertyChanged(nameof(IsEmotionLetterSearchModule));
        OnPropertyChanged(nameof(IsEmotionStroopModule));
        OnPropertyChanged(nameof(IsBasicInfoModule));
        OnPropertyChanged(nameof(IsQuestionnaireModule));
        OnPropertyChanged(nameof(IsFormModule));
        OnPropertyChanged(nameof(IsCaptureTaskModule));
        OnPropertyChanged(nameof(IsSyncTestModule));
        OnPropertyChanged(nameof(IsDevelopmentModuleOverride));
        OnPropertyChanged(nameof(IsEyeCalibrationStage));
        OnPropertyChanged(nameof(IsPictureBrowseStage));
        OnPropertyChanged(nameof(IsVideoBrowseStage));
        OnPropertyChanged(nameof(IsVoiceBaselineStage));
        OnPropertyChanged(nameof(IsWordReadingStage));
        OnPropertyChanged(nameof(IsShortTextReadingStage));
        OnPropertyChanged(nameof(IsEmotionQuestionStage));
        OnPropertyChanged(nameof(IsDotProbeStage));
        OnPropertyChanged(nameof(IsEmotionOddballStage));
        OnPropertyChanged(nameof(IsEmotionLetterSearchStage));
        OnPropertyChanged(nameof(IsEmotionStroopStage));
        OnPropertyChanged(nameof(IsBasicInfoStage));
        OnPropertyChanged(nameof(IsQuestionnaireStage));
        OnPropertyChanged(nameof(IsSyncTestStage));
        OnPropertyChanged(nameof(IsPictureShowing));
        OnPropertyChanged(nameof(IsPictureFixation));
        OnPropertyChanged(nameof(IsPictureBlank));
        OnPropertyChanged(nameof(IsPictureResting));
        OnPropertyChanged(nameof(ShowPictureStatusBadge));
        OnPropertyChanged(nameof(IsVideoBrowseBlank));
        OnPropertyChanged(nameof(IsVideoBrowsePlaying));
        OnPropertyChanged(nameof(IsVideoBrowseResting));
        OnPropertyChanged(nameof(ShowVideoStatusBadge));
        OnPropertyChanged(nameof(IsVoiceBaselineWaiting));
        OnPropertyChanged(nameof(IsVoiceBaselinePreparing));
        OnPropertyChanged(nameof(IsVoiceBaselineRecording));
        OnPropertyChanged(nameof(IsVoiceBaselineResting));
        OnPropertyChanged(nameof(IsVoiceBaselinePromptVisible));
        OnPropertyChanged(nameof(ShowVoiceBaselineStartAction));
        OnPropertyChanged(nameof(CanFinishVoiceBaselineSegment));
        OnPropertyChanged(nameof(VoiceBaselineFinishButtonText));
        OnPropertyChanged(nameof(IsWordReadingWaiting));
        OnPropertyChanged(nameof(IsWordReadingActive));
        OnPropertyChanged(nameof(IsWordReadingResting));
        OnPropertyChanged(nameof(IsWordReadingPromptVisible));
        OnPropertyChanged(nameof(ShowWordReadingStartAction));
        OnPropertyChanged(nameof(IsShortTextReadingWaiting));
        OnPropertyChanged(nameof(IsShortTextReadingActive));
        OnPropertyChanged(nameof(IsShortTextReadingResting));
        OnPropertyChanged(nameof(IsShortTextReadingPostBlank));
        OnPropertyChanged(nameof(IsShortTextReadingPromptVisible));
        OnPropertyChanged(nameof(IsShortTextReadingCountdown));
        OnPropertyChanged(nameof(IsShortTextReadingContentVisible));
        OnPropertyChanged(nameof(ShortTextReadingTitleText));
        OnPropertyChanged(nameof(ShortTextReadingPassageFontSize));
        OnPropertyChanged(nameof(ShortTextReadingPassageTextAlignment));
        OnPropertyChanged(nameof(ShowShortTextReadingStartAction));
        OnPropertyChanged(nameof(CanExecuteShortTextReadingAction));
        OnPropertyChanged(nameof(ShortTextReadingCountdownDisplayText));
        OnPropertyChanged(nameof(ShortTextReadingStartButtonText));
        OnPropertyChanged(nameof(ShortTextReadingPassageTitleText));
        if (StartShortTextReadingCommand is AsyncRelayCommand shortTextCommand)
        {
            shortTextCommand.RaiseCanExecuteChanged();
        }
        OnPropertyChanged(nameof(IsEmotionQuestionWaiting));
        OnPropertyChanged(nameof(IsEmotionQuestionAnswering));
        OnPropertyChanged(nameof(IsEmotionQuestionPromptVisible));
        OnPropertyChanged(nameof(IsDotProbePreBlank));
        OnPropertyChanged(nameof(IsDotProbeFixation));
        OnPropertyChanged(nameof(IsDotProbePictures));
        OnPropertyChanged(nameof(IsDotProbePostBlank));
        OnPropertyChanged(nameof(IsDotProbeProbe));
        OnPropertyChanged(nameof(IsDotProbeResting));
        OnPropertyChanged(nameof(IsDotProbeProbeTop));
        OnPropertyChanged(nameof(IsDotProbeProbeBottom));
        OnPropertyChanged(nameof(ShowDotProbeResponseButtons));
        NotifyEmotionOddballStateChanged();
        NotifyEmotionLetterSearchStateChanged();
        NotifyEmotionStroopStateChanged();
        OnPropertyChanged(nameof(IsFallbackStage));
        OnPropertyChanged(nameof(IsCompletionStage));
        OnPropertyChanged(nameof(IsSavingStage));
        OnPropertyChanged(nameof(IsModuleSaveFailed));
        OnPropertyChanged(nameof(IsModuleSavingInProgress));
        OnPropertyChanged(nameof(SavingStageTitleText));
        OnPropertyChanged(nameof(SavingStageDescriptionText));
        OnPropertyChanged(nameof(IsGenericFallbackStage));
        OnPropertyChanged(nameof(ShowDemoPlayAction));
        OnPropertyChanged(nameof(ShowDevelopmentSkipDemoAction));
        OnPropertyChanged(nameof(ShowFaceCheckAction));
        OnPropertyChanged(nameof(FaceReadinessTitleText));
        OnPropertyChanged(nameof(IsFaceReady));
        OnPropertyChanged(nameof(FaceReadinessProgressPercent));
        OnPropertyChanged(nameof(FaceReadinessBadgeText));
        OnPropertyChanged(nameof(FaceReadinessStatusText));
        OnPropertyChanged(nameof(FaceReadinessAccentBrush));
        OnPropertyChanged(nameof(ShowSyncTestStartAction));
        OnPropertyChanged(nameof(ShowSyncTestRunning));
        OnPropertyChanged(nameof(IsSyncTestRecordingActive));
        OnPropertyChanged(nameof(IsQuestionnaireInProgress));
        OnPropertyChanged(nameof(WorkbenchLeaveWarningTitle));
        OnPropertyChanged(nameof(QuestionnaireLeaveWarningTitle));
        OnPropertyChanged(nameof(QuestionnaireLeaveWarningMessage));
        OnPropertyChanged(nameof(WorkbenchLeaveConfirmText));
        OnPropertyChanged(nameof(WorkbenchLeaveCancelText));
        OnPropertyChanged(nameof(SyncTestTitleText));
        OnPropertyChanged(nameof(SyncTestStartButtonText));
        OnPropertyChanged(nameof(StageNoticeText));
        OnPropertyChanged(nameof(HasStageNotice));
        OnPropertyChanged(nameof(CanStartCalibration));
        OnPropertyChanged(nameof(IsPrepareStep));
        OnPropertyChanged(nameof(IsDemoStep));
        OnPropertyChanged(nameof(IsFaceStep));
        OnPropertyChanged(nameof(IsCalibrationStep));
        OnPropertyChanged(nameof(IsImageBrowseStep));
        OnPropertyChanged(nameof(PrepareStepBrush));
        OnPropertyChanged(nameof(DemoStepBrushValue));
        OnPropertyChanged(nameof(FaceStepBrush));
        OnPropertyChanged(nameof(CalibrationStepBrush));
        OnPropertyChanged(nameof(ImageBrowseStepBrush));
        OnPropertyChanged(nameof(FormFillStepBrush));
        OnPropertyChanged(nameof(FormCompletedStepBrush));
        OnPropertyChanged(nameof(PrepareStepTextBrush));
        OnPropertyChanged(nameof(DemoStepTextBrush));
        OnPropertyChanged(nameof(FaceStepTextBrush));
        OnPropertyChanged(nameof(CalibrationStepTextBrush));
        OnPropertyChanged(nameof(ImageBrowseStepTextBrush));
        OnPropertyChanged(nameof(FormFillStepTextBrush));
        OnPropertyChanged(nameof(FormCompletedStepTextBrush));
        OnPropertyChanged(nameof(PictureBrowseImagePath));
        OnPropertyChanged(nameof(CurrentPictureBrowseImageType));
        OnPropertyChanged(nameof(PictureBrowseStatusText));
        OnPropertyChanged(nameof(PictureBrowseRestText));
        OnPropertyChanged(nameof(VideoBrowseVideoPath));
        OnPropertyChanged(nameof(VideoBrowseVideoUri));
        OnPropertyChanged(nameof(CurrentVideoBrowseVideoType));
        OnPropertyChanged(nameof(VideoBrowseStatusText));
        OnPropertyChanged(nameof(VideoBrowseRestText));
        OnPropertyChanged(nameof(VoiceBaselinePromptText));
        OnPropertyChanged(nameof(VoiceBaselineSyllableText));
        OnPropertyChanged(nameof(VoiceBaselineTitleText));
        OnPropertyChanged(nameof(VoiceBaselineStartButtonText));
        OnPropertyChanged(nameof(WordReadingTitleText));
        OnPropertyChanged(nameof(WordReadingStartButtonText));
        OnPropertyChanged(nameof(WordReadingGroupTitleText));
        OnPropertyChanged(nameof(WordReadingCurrentWords));
        OnPropertyChanged(nameof(ShortTextReadingTitleText));
        OnPropertyChanged(nameof(ShortTextReadingStartButtonText));
        OnPropertyChanged(nameof(ShortTextReadingPassageTitleText));
        OnPropertyChanged(nameof(ShortTextReadingPassageText));
        OnPropertyChanged(nameof(EmotionQuestionTitleText));
        OnPropertyChanged(nameof(EmotionQuestionStartButtonText));
        OnPropertyChanged(nameof(EmotionQuestionSubmitButtonText));
        OnPropertyChanged(nameof(EmotionQuestionSubmitHintText));
        OnPropertyChanged(nameof(CanCompleteEmotionQuestionAnswer));
        OnPropertyChanged(nameof(EmotionQuestionProgressText));
        OnPropertyChanged(nameof(EmotionQuestionText));
        OnPropertyChanged(nameof(RestTitleText));
        OnPropertyChanged(nameof(VoiceBaselineStatusText));
        OnPropertyChanged(nameof(VoiceBaselineRestText));
        OnPropertyChanged(nameof(WordReadingStatusText));
        OnPropertyChanged(nameof(WordReadingRestText));
        OnPropertyChanged(nameof(ShortTextReadingStatusText));
        OnPropertyChanged(nameof(ShortTextReadingRestText));
        OnPropertyChanged(nameof(EmotionQuestionStatusText));
        OnPropertyChanged(nameof(DotProbeTopImagePath));
        OnPropertyChanged(nameof(DotProbeBottomImagePath));
        OnPropertyChanged(nameof(DotProbeRestTitleText));
        OnPropertyChanged(nameof(DotProbeRestText));
        OnPropertyChanged(nameof(DotProbeUpText));
        OnPropertyChanged(nameof(DotProbeDownText));
        OnPropertyChanged(nameof(SelectedBasicInfoGender));
        OnPropertyChanged(nameof(SelectedBasicInfoGenderDisplay));
        OnPropertyChanged(nameof(BasicInfoBirthDateText));
        OnPropertyChanged(nameof(BasicInfoBirthDateDisplay));
        OnPropertyChanged(nameof(SelectedBasicInfoEducation));
        OnPropertyChanged(nameof(SelectedBasicInfoEducationDisplay));
        OnPropertyChanged(nameof(SelectedBasicInfoOccupation));
        OnPropertyChanged(nameof(SelectedBasicInfoOccupationDisplay));
        OnPropertyChanged(nameof(SelectedBasicInfoIncomeLevel));
        OnPropertyChanged(nameof(SelectedBasicInfoIncomeLevelDisplay));
        OnPropertyChanged(nameof(BasicInfoValidationMessage));
        OnPropertyChanged(nameof(HasBasicInfoValidationMessage));
        OnPropertyChanged(nameof(BasicInfoFormTitleText));
        OnPropertyChanged(nameof(BasicInfoFormDescriptionText));
        OnPropertyChanged(nameof(BasicInfoSubmitButtonText));
        OnPropertyChanged(nameof(BasicInfoCompletedText));
        OnPropertyChanged(nameof(BasicInfoGenderLabelText));
        OnPropertyChanged(nameof(BasicInfoBirthDateLabelText));
        OnPropertyChanged(nameof(BasicInfoBirthDateHintText));
        OnPropertyChanged(nameof(BasicInfoEducationLabelText));
        OnPropertyChanged(nameof(BasicInfoOccupationLabelText));
        OnPropertyChanged(nameof(BasicInfoIncomeLevelLabelText));
        OnPropertyChanged(nameof(BasicInfoEditOptionText));
        OnPropertyChanged(nameof(BasicInfoChooseOneOptionText));
        OnPropertyChanged(nameof(QuestionnaireTitleText));
        OnPropertyChanged(nameof(QuestionnaireSubtitleText));
        OnPropertyChanged(nameof(QuestionnaireInstructionText));
        OnPropertyChanged(nameof(QuestionnaireSubmitButtonText));
        OnPropertyChanged(nameof(QuestionnaireCompletedText));
        OnPropertyChanged(nameof(CurrentQuestionnaireQuestion));
        OnPropertyChanged(nameof(CurrentQuestionnaireQuestionNumber));
        OnPropertyChanged(nameof(QuestionnaireQuestionCount));
        OnPropertyChanged(nameof(QuestionnaireProgressText));
        OnPropertyChanged(nameof(QuestionnairePreviousButtonText));
        OnPropertyChanged(nameof(QuestionnaireNextButtonText));
        OnPropertyChanged(nameof(CanGoPreviousQuestionnaireQuestion));
        OnPropertyChanged(nameof(CanGoNextQuestionnaireQuestion));
        OnPropertyChanged(nameof(ShowQuestionnaireNextButton));
        OnPropertyChanged(nameof(ShowQuestionnaireSubmitButton));
        OnPropertyChanged(nameof(IsQuestionnaireOptionPanelOpen));
        OnPropertyChanged(nameof(QuestionnaireOptionTitle));
        OnPropertyChanged(nameof(QuestionnaireValidationMessage));
        OnPropertyChanged(nameof(HasQuestionnaireValidationMessage));
        OnPropertyChanged(nameof(CancelText));
        OnPropertyChanged(nameof(SyncTestStatusText));
    }

    private Brush StepBrush(int stepIndex)
    {
        if ((int)currentStep != stepIndex)
        {
            return InactiveStepBrush;
        }

        return stepIndex == 1 ? DemoStepBrush : ActiveStepBrush;
    }

    private Brush StepTextBrush(int stepIndex)
    {
        return (int)currentStep == stepIndex ? ActiveTextBrush : InactiveTextBrush;
    }

    private Brush FormStepBrush(CaptureWorkbenchStep step)
    {
        return currentStep == step ? ActiveStepBrush : InactiveStepBrush;
    }

    private Brush FormStepTextBrush(CaptureWorkbenchStep step)
    {
        return currentStep == step ? ActiveTextBrush : InactiveTextBrush;
    }

    private static string FormatTime(TimeSpan value)
    {
        return value.TotalHours >= 1
            ? value.ToString(@"hh\:mm\:ss")
            : value.ToString(@"mm\:ss");
    }

    /// <summary>
    /// 采集工作台统一从多语言服务取文案。
    /// 后续新增模块时不要在 ViewModel / XAML 中直接写死可见文本，应优先在 AppLocalizationService 中加 key。
    /// </summary>
    private string T(string key, params object[] args)
    {
        var text = localization.Text(key);
        return args.Length == 0 ? text : string.Format(text, args);
    }

    public string Localize(string key, params object[] args)
    {
        return T(key, args);
    }

    private static string ResolveAssetPath(params string[] segments)
    {
        var pathSegments = new string[segments.Length + 1];
        pathSegments[0] = AppContext.BaseDirectory;
        Array.Copy(segments, 0, pathSegments, 1, segments.Length);
        return Path.Combine(pathSegments);
    }
}
