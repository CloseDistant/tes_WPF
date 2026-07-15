namespace RuinaoHardwareDebugWpf;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

/// <summary>
/// 采集工作台 ViewModel。
/// 负责采集模块流程状态、中央显示区状态、按钮显隐、模块进度和录制状态展示。
/// 摄像头预览、音视频录制和数据库写入分别由 View 与服务层处理。
/// </summary>
public sealed partial class AssessmentCaptureViewModel : ObservableObject
{

    private readonly ILocalizationService localization;
    private readonly IUserDialogService userDialogService;
    private readonly IModuleEventRecorder moduleEventRecorder;
    private readonly IUnifiedSessionService unifiedSessionService;
    private readonly IPatientService patientService;
    private readonly AssessmentWorkbenchCoordinator workbenchCoordinator;
    private readonly DispatcherTimer calibrationTimer = new();
    private readonly DispatcherTimer pictureBrowseTimer = new();
    private readonly DispatcherTimer videoBrowseTimer = new();
    private readonly DispatcherTimer voiceBaselineTimer = new();
    private readonly DispatcherTimer wordReadingTimer = new();
    private readonly DispatcherTimer syncTestTimer = new();
    private readonly Random videoBrowseRandom = new();
    private readonly Queue<CalibrationFrame> calibrationFrames = new();
    private static readonly Brush ActiveStepBrush = new SolidColorBrush(Color.FromRgb(208, 144, 62));
    private static readonly Brush DemoStepBrush = new SolidColorBrush(Color.FromRgb(75, 119, 216));
    private static readonly Brush InactiveStepBrush = new SolidColorBrush(Color.FromRgb(48, 54, 69));
    private static readonly Brush ActiveTextBrush = new SolidColorBrush(Color.FromRgb(228, 232, 239));
    private static readonly Brush InactiveTextBrush = new SolidColorBrush(Color.FromRgb(142, 150, 168));
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
        currentStep = step;
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
    private string calibrationStatusText = "待开始";
    private string frameSaveStatusText = string.Empty;
    private string frameOutputDirectory = string.Empty;
    private string stageNoticeText = string.Empty;
    private string pictureBrowseImagePath = string.Empty;
    private string pictureBrowseStatusText = "待开始";
    private string pictureBrowseRestText = string.Empty;
    private PictureBrowsePhase pictureBrowsePhase = PictureBrowsePhase.Idle;
    private int pictureBrowseIndex;
    private int pictureBrowseRestRemainingSeconds;
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

    public AssessmentCaptureViewModel(
        ICaptureMediaRecorder captureMediaRecorder,
        ILocalizationService localization,
        IUserDialogService userDialogService,
        IModuleEventRecorder moduleEventRecorder,
        IUnifiedSessionService unifiedSessionService,
        IPatientService patientService,
        AssessmentWorkbenchCoordinator workbenchCoordinator)
    {
        CaptureMediaRecorder = captureMediaRecorder;
        this.localization = localization;
        this.userDialogService = userDialogService;
        this.moduleEventRecorder = moduleEventRecorder;
        this.unifiedSessionService = unifiedSessionService;
        this.patientService = patientService;
        this.workbenchCoordinator = workbenchCoordinator;
        this.localization.LanguageChanged += (_, _) =>
        {
            RefreshModuleDisplayNames();
            NotifyStageChanged();
        };
        CaptureMediaRecorder.RecordingCompleted += OnRecordingCompleted;
        DevNextStepCommand = new RelayCommand(_ => MoveToNextDevStep());
        GoNextModuleCommand = new RelayCommand(_ => GoNextModule());
        SwitchModuleCommand = new RelayCommand(SwitchModule);
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
        StartShortTextReadingCommand = new RelayCommand(_ => StartShortTextReadingFirstPassage());
        InitializeEmotionQuestionModule();
        InitializeDotProbeModule();
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
        shortTextReadingTimer.Interval = TimeSpan.FromSeconds(1);
        shortTextReadingTimer.Tick += (_, _) => AdvanceShortTextReading();
        syncTestTimer.Interval = TimeSpan.FromSeconds(1);
        syncTestTimer.Tick += (_, _) => AdvanceSyncTest();
        LoadCameraDevices();
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

    public ICaptureMediaRecorder CaptureMediaRecorder { get; }

    public string DemoVideoPath => CurrentModuleCode switch
    {
        PictureBrowseModuleCode => ResolveAssetPath("Assets", "CaptureWorkbench", "Videos", "PictureBrowseDemo.mp4"),
        VideoBrowseModuleCode => ResolveAssetPath("Assets", "CaptureWorkbench", "Videos", "VideoBrowseDemo.mp4"),
        VoiceBaselineModuleCode => ResolveAssetPath("Assets", "CaptureWorkbench", "Videos", "VoiceBaselineDemo.mp4"),
        WordReadingModuleCode => ResolveAssetPath("Assets", "CaptureWorkbench", "Videos", "WordReadingDemo.mp4"),
        ShortTextReadingModuleCode => ResolveAssetPath("Assets", "CaptureWorkbench", "Videos", "ShortTextReadingDemo.mp4"),
        EmotionQuestionModuleCode => ResolveAssetPath("Assets", "CaptureWorkbench", "Videos", "EmotionQuestionDemo.mp4"),
        DotProbeModuleCode => ResolveAssetPath("Assets", "CaptureWorkbench", "Videos", "DotProbeDemo.mp4"),
        _ => ResolveAssetPath("Assets", "CaptureWorkbench", "Videos", "EyeCalibrationDemo.mp4")
    };

    public Uri DemoVideoUri => new(DemoVideoPath);

    public string CurrentModuleCode => ModuleProgressItems.Count == 0
        ? EyeCalibrationModuleCode
        : ModuleProgressItems[currentModuleIndex].Code;

    public string CurrentModule => ModuleProgressItems.Count == 0
        ? T("ModuleEyeCalibration")
        : ModuleProgressItems[currentModuleIndex].Name;

    public string NextModule => currentModuleIndex + 1 < ModuleProgressItems.Count
        ? ModuleProgressItems[currentModuleIndex + 1].Name
        : T("CaptureWorkspaceEnd");

    public string PrimaryActionText => isDemoCompleted ? T("CaptureWorkspaceReplayDemo") : T("CaptureWorkspacePlayDemo");

    public string SecondaryActionText => T("CaptureWorkspaceStart");

    public string WorkspaceTitleText => T("AssessmentCapture");

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

    public string DevelopmentClickableText => T("CaptureWorkspaceDevelopmentClickable");

    public ICommand DevNextStepCommand { get; }

    public ICommand GoNextModuleCommand { get; }

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

    public bool IsDemoPlaying => isDemoPlaying;

    public bool IsDemoCompleted => isDemoCompleted;

    public bool IsCalibrationStage => currentStep == CaptureWorkbenchStep.ModuleExecution;

    /// <summary>
    /// 当前是否处于模块正式采集阶段。
    /// 离开采集工作台前会读取该状态，避免用户误切页面导致本次未完成录制被丢弃。
    /// </summary>
    public bool IsExecutingCaptureTask => currentStep == CaptureWorkbenchStep.ModuleExecution && !IsCompletionStage && !IsFormModule;

    public bool IsQuestionnaireInProgress => IsQuestionnaireStage;

    public bool ShouldConfirmLeavingWorkbench => IsDemoPlaying || IsExecutingCaptureTask || IsQuestionnaireInProgress;

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

    public bool IsBasicInfoStage => IsCalibrationStage && IsBasicInfoModule;

    public bool IsQuestionnaireStage => IsCalibrationStage && IsQuestionnaireModule;

    public bool IsSyncTestStage => IsCalibrationStage && IsSyncTestModule;

    public bool IsPictureShowing => IsPictureBrowseStage && pictureBrowsePhase == PictureBrowsePhase.ShowingImage;

    public bool IsPictureBlank => IsPictureBrowseStage && pictureBrowsePhase == PictureBrowsePhase.Blank;

    public bool IsPictureResting => IsPictureBrowseStage && pictureBrowsePhase == PictureBrowsePhase.Resting;

    public bool ShowPictureStatusBadge => IsPictureShowing;

    public bool IsVideoBrowseBlank => IsVideoBrowseStage && videoBrowsePhase == VideoBrowsePhase.Blank;

    public bool IsVideoBrowsePlaying => IsVideoBrowseStage && videoBrowsePhase == VideoBrowsePhase.PlayingVideo;

    public bool IsVideoBrowseResting => IsVideoBrowseStage && videoBrowsePhase == VideoBrowsePhase.Resting;

    public bool ShowVideoStatusBadge => IsVideoBrowsePlaying;

    public bool IsVoiceBaselineWaiting => IsVoiceBaselineStage && voiceBaselinePhase == VoiceBaselinePhase.WaitingToStart;

    public bool IsVoiceBaselineRecording => IsVoiceBaselineStage && voiceBaselinePhase == VoiceBaselinePhase.Recording;

    public bool IsVoiceBaselineResting => IsVoiceBaselineStage && voiceBaselinePhase == VoiceBaselinePhase.Resting;

    public bool IsVoiceBaselinePromptVisible => IsVoiceBaselineStage && voiceBaselinePhase != VoiceBaselinePhase.Resting;

    public bool ShowVoiceBaselineStartAction => IsVoiceBaselineWaiting && voiceBaselineIndex == 0;

    public bool IsWordReadingWaiting => IsWordReadingStage && wordReadingPhase == WordReadingPhase.WaitingToStart;

    public bool IsWordReadingActive => IsWordReadingStage && wordReadingPhase == WordReadingPhase.Reading;

    public bool IsWordReadingResting => IsWordReadingStage && wordReadingPhase == WordReadingPhase.Resting;

    public bool IsWordReadingPromptVisible => IsWordReadingStage && wordReadingPhase != WordReadingPhase.Resting;

    public bool ShowWordReadingStartAction => IsWordReadingWaiting && wordReadingIndex == 0;

    public bool IsFallbackStage => !IsDemoStage && !IsEyeCalibrationStage && !IsPictureBrowseStage && !IsVideoBrowseStage && !IsVoiceBaselineStage && !IsWordReadingStage && !IsShortTextReadingStage && !IsEmotionQuestionStage && !IsDotProbeStage && !IsBasicInfoStage && !IsQuestionnaireStage && !IsSyncTestStage;

    public bool IsCompletionStage => currentStep == CaptureWorkbenchStep.Completed;

    public bool IsGenericFallbackStage => IsFallbackStage && !IsCompletionStage;

    public bool ShowDemoPlayAction => IsDemoStep && !isDemoPlaying && !isDemoCompleted;

    public bool ShowFaceCheckAction => IsDemoStep && isDemoCompleted && !IsFormModule;

    public bool ShowModuleStartAction => IsFaceStep && !IsFormModule;

    public bool ShowSyncTestStartAction => IsSyncTestStage && !isSyncTestRunning && syncTestRemainingSeconds == SyncTestDurationSeconds;

    public bool ShowSyncTestRunning => IsSyncTestStage && isSyncTestRunning;

    public bool IsSyncTestRecordingActive => IsSyncTestStage && isSyncTestRunning;

    public bool CanStartCalibration => isDemoCompleted && (currentStep is CaptureWorkbenchStep.Demo or CaptureWorkbenchStep.FaceCheck);

    public bool HasStageNotice => !string.IsNullOrWhiteSpace(stageNoticeText);

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

    public double CalibrationCanvasLeft => (CalibrationX / 100d * 760d) - 22d;

    public double CalibrationCanvasTop => (CalibrationY / 100d * 460d) - 22d;

    public string CalibrationStatusText
    {
        get => calibrationStatusText;
        private set => SetProperty(ref calibrationStatusText, value);
    }

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
        CaptureWorkbenchStep.ModuleExecution when IsPictureBrowseModule => T("CaptureWorkspaceMainPictureBrowse"),
        CaptureWorkbenchStep.ModuleExecution when IsVideoBrowseModule => T("CaptureWorkspaceMainVideoBrowse"),
        CaptureWorkbenchStep.ModuleExecution => T("CaptureWorkspaceMainEyeCalibration"),
        CaptureWorkbenchStep.Completed => T("CaptureWorkspaceMainCompleted", NextModule),
        _ => T("CaptureWorkspaceMainDemo")
    };

    public string DevHintText => currentStep == CaptureWorkbenchStep.ModuleExecution
        ? T("CaptureWorkspaceDevHintExecution")
        : T("CaptureWorkspaceDevHintDefault");

    public string StartButtonStateText => currentStep switch
    {
        CaptureWorkbenchStep.Demo when isDemoCompleted => T("CaptureWorkspaceStartButtonToFace"),
        CaptureWorkbenchStep.FaceCheck => T("CaptureWorkspaceStartButtonToExecution"),
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
        savedFrameCount = 0;
        frameOutputDirectory = string.Empty;
        FrameSaveStatusText = T("CaptureWorkspaceRecordingPending");
        OnPropertyChanged(nameof(FrameOutputDirectory));
    }

    public void DiscardFrameSavingStatus()
    {
        savedFrameCount = 0;
        frameOutputDirectory = string.Empty;
        FrameSaveStatusText = T("CaptureWorkspaceRecordingDiscarded");
        OnPropertyChanged(nameof(FrameOutputDirectory));
    }

    public void AbortCurrentModuleExecution(string message)
    {
        if (currentStep != CaptureWorkbenchStep.ModuleExecution)
        {
            return;
        }

        StopModuleExecutionTimers();
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
        NotifyStageChanged();
    }

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
        if (!isDemoCompleted)
        {
            return;
        }

        MoveToStep(CaptureWorkbenchStep.FaceCheck);
        isDemoPlaying = false;
        StopModuleExecutionTimers();
        StageNoticeText = T("CaptureWorkspaceFaceCheckNotice");
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
    /// 第一段语音由用户手动点击开始。
    /// 后续语音段由休息倒计时结束后自动开始，不再出现手动按钮。
    /// </summary>
    public void StartVoiceBaselineFirstSegment()
    {
        if (!IsVoiceBaselineWaiting || voiceBaselineIndex != 0)
        {
            return;
        }

        StartVoiceBaselineSegment();
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
        FrameSaveStatusText = T("CaptureWorkspaceRecordingActive");
        OnPropertyChanged(nameof(FrameOutputDirectory));
    }

    public void RecordSavedFrame()
    {
        savedFrameCount++;
        FrameSaveStatusText = T("CaptureWorkspaceRecordingFrameCount", savedFrameCount);
    }

    public void StopFrameSaving()
    {
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

    private void OnRecordingCompleted(object? sender, CaptureRecordingCompletedEventArgs args)
    {
        // 录制合成在后台线程完成，需要切回 UI 线程更新界面状态。
        // 如果用户已经切换到别的模块，不把上一模块的完成状态覆盖到当前模块。
        void ApplyResult()
        {
            if (!string.Equals(CurrentModuleCode, args.Session.ModuleCode, StringComparison.Ordinal))
            {
                return;
            }

            if (args.Status == "completed")
            {
                CompleteMergedVideo();
                return;
            }

            if (args.Status == "completed_with_probe_error")
            {
                CompleteMergedVideoWithProbeError();
                return;
            }

            if (args.Status == "merge_failed")
            {
                FailMergedVideo("请检查 FFmpeg");
                return;
            }

            if (args.Status == "discarded")
            {
                DiscardFrameSavingStatus();
            }
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplyResult();
        }
        else
        {
            dispatcher.Invoke(ApplyResult);
        }
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

    private void GoNextModule()
    {
        if (currentModuleIndex + 1 >= ModuleProgressItems.Count)
        {
            return;
        }

        currentModuleIndex++;
        MoveToStep(IsFormModuleCode(CurrentModuleCode) ? CaptureWorkbenchStep.ModuleExecution : CaptureWorkbenchStep.Demo);
        isDemoCompleted = IsFormModule;
        isDemoPlaying = false;
        StopModuleExecutionTimers();
        PlaybackTimeText = "00:00 / 未播放";
        CalibrationStatusText = "待开始";
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
    }

    /// <summary>
    /// 开发阶段允许点击右侧模块流程直接切换模块。
    /// 切换模块只重置当前模块内步骤，不清空整个采集工作台结果。
    /// </summary>
    private void SwitchModule(object? parameter)
    {
        if (parameter is not ModuleProgressItem item)
        {
            return;
        }

        if (item.Index != currentModuleIndex && IsQuestionnaireInProgress && !ConfirmDiscardActiveQuestionnaire())
        {
            return;
        }

        currentModuleIndex = item.Index;
        MoveToStep(item.Code == SyncTestModuleCode || IsFormModuleCode(item.Code) ? CaptureWorkbenchStep.ModuleExecution : CaptureWorkbenchStep.Demo);
        isDemoCompleted = item.Code == SyncTestModuleCode || IsFormModuleCode(item.Code);
        isDemoPlaying = false;
        StopModuleExecutionTimers();
        PlaybackTimeText = "00:00 / 未播放";
        CalibrationStatusText = "待开始";
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
        workbenchCoordinator.Configure(CaptureWorkbenchModules.Select(module =>
            (module.Code, module.DisplayNameKey, module.IsDevelopmentOnly)));
        ModuleProgressItems.Clear();
        for (var i = 0; i < workbenchCoordinator.Modules.Count; i++)
        {
            var module = workbenchCoordinator.Modules[i];
            ModuleProgressItems.Add(new ModuleProgressItem(
                i,
                module.Code,
                module.DisplayNameKey,
                T(module.DisplayNameKey),
                module.IsDevelopmentOnly));
        }

        UpdateModuleProgressItems();
    }

    private void UpdateModuleProgressItems()
    {
        foreach (var item in ModuleProgressItems)
        {
            item.UpdateState(
                currentModuleIndex,
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
        voiceBaselineRemainingSeconds = VoiceBaselineSegmentSeconds;
        currentVoiceBaselineStartedAt = null;
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

    public Task FlushPendingModuleEventsAsync(CancellationToken cancellationToken = default)
        => moduleEventRecorder.FlushAsync(cancellationToken);

    /// <summary>
    /// 清理图片浏览内部状态。
    /// 用于切换模块、重播演示或重新开始当前模块。
    /// </summary>
    private void ResetPictureBrowseState()
    {
        pictureBrowseTimer.Stop();
        pictureBrowsePhase = PictureBrowsePhase.Idle;
        pictureBrowseIndex = 0;
        pictureBrowseRestRemainingSeconds = 0;
        PictureBrowseImagePath = string.Empty;
        CurrentPictureBrowseImageType = null;
        PictureBrowseStatusText = "待开始";
        PictureBrowseRestText = string.Empty;
    }

    /// <summary>
    /// 停止当前模块第三步内部计时器。
    /// 注意：只停止流程计时，不处理音视频录制；录制由 ICaptureMediaRecorder 管理。
    /// </summary>
    private void StopModuleExecutionTimers()
    {
        calibrationTimer.Stop();
        ResetPictureBrowseState();
        ResetVideoBrowseState();
        ResetVoiceBaselineState();
        ResetWordReadingState();
        ResetShortTextReadingState();
        ResetEmotionQuestionState();
        ResetDotProbeState();
        ResetSyncTestState();
    }

    private static (double X, double Y) PositionForFixedPoint(int pointNumber, int[] numberAtPositions)
    {
        var positions = new (double X, double Y)[]
        {
            (18, 18), (50, 18), (82, 18),
            (18, 50), (82, 50),
            (18, 82), (50, 82), (82, 82)
        };

        var positionIndex = Array.IndexOf(numberAtPositions, pointNumber);
        return positionIndex >= 0 && positionIndex < positions.Length
            ? positions[positionIndex]
            : (50, 50);
    }

    private static (double X, double Y) PositionForRegionPoint(int pointNumber, int region)
    {
        var slot = (pointNumber - 1) % 5;
        var x = slot switch
        {
            0 => 18d,
            1 => 34d,
            2 => 50d,
            3 => 66d,
            _ => 82d
        };

        var y = region == 1 ? 28d : 72d;
        return (x, y);
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
        OnPropertyChanged(nameof(CurrentModuleCode));
        OnPropertyChanged(nameof(NextModule));
        OnPropertyChanged(nameof(SharedDisplayTitle));
        OnPropertyChanged(nameof(EnterFaceCheckButtonText));
        OnPropertyChanged(nameof(CameraPanelTitleText));
        OnPropertyChanged(nameof(RefreshButtonText));
        OnPropertyChanged(nameof(CameraPreviewPlaceholderText));
        OnPropertyChanged(nameof(ModuleFlowTitleText));
        OnPropertyChanged(nameof(DevelopmentClickableText));
        OnPropertyChanged(nameof(DemoVideoPath));
        OnPropertyChanged(nameof(DemoVideoUri));
        OnPropertyChanged(nameof(DevMainStageText));
        OnPropertyChanged(nameof(DevHintText));
        OnPropertyChanged(nameof(StartButtonStateText));
        OnPropertyChanged(nameof(PrimaryActionText));
        OnPropertyChanged(nameof(IsDemoStage));
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
        OnPropertyChanged(nameof(IsBasicInfoModule));
        OnPropertyChanged(nameof(IsQuestionnaireModule));
        OnPropertyChanged(nameof(IsFormModule));
        OnPropertyChanged(nameof(IsCaptureTaskModule));
        OnPropertyChanged(nameof(IsSyncTestModule));
        OnPropertyChanged(nameof(IsEyeCalibrationStage));
        OnPropertyChanged(nameof(IsPictureBrowseStage));
        OnPropertyChanged(nameof(IsVideoBrowseStage));
        OnPropertyChanged(nameof(IsVoiceBaselineStage));
        OnPropertyChanged(nameof(IsWordReadingStage));
        OnPropertyChanged(nameof(IsShortTextReadingStage));
        OnPropertyChanged(nameof(IsEmotionQuestionStage));
        OnPropertyChanged(nameof(IsDotProbeStage));
        OnPropertyChanged(nameof(IsBasicInfoStage));
        OnPropertyChanged(nameof(IsQuestionnaireStage));
        OnPropertyChanged(nameof(IsSyncTestStage));
        OnPropertyChanged(nameof(IsPictureShowing));
        OnPropertyChanged(nameof(IsPictureBlank));
        OnPropertyChanged(nameof(IsPictureResting));
        OnPropertyChanged(nameof(ShowPictureStatusBadge));
        OnPropertyChanged(nameof(IsVideoBrowseBlank));
        OnPropertyChanged(nameof(IsVideoBrowsePlaying));
        OnPropertyChanged(nameof(IsVideoBrowseResting));
        OnPropertyChanged(nameof(ShowVideoStatusBadge));
        OnPropertyChanged(nameof(IsVoiceBaselineWaiting));
        OnPropertyChanged(nameof(IsVoiceBaselineRecording));
        OnPropertyChanged(nameof(IsVoiceBaselineResting));
        OnPropertyChanged(nameof(IsVoiceBaselinePromptVisible));
        OnPropertyChanged(nameof(ShowVoiceBaselineStartAction));
        OnPropertyChanged(nameof(IsWordReadingWaiting));
        OnPropertyChanged(nameof(IsWordReadingActive));
        OnPropertyChanged(nameof(IsWordReadingResting));
        OnPropertyChanged(nameof(IsWordReadingPromptVisible));
        OnPropertyChanged(nameof(ShowWordReadingStartAction));
        OnPropertyChanged(nameof(IsShortTextReadingWaiting));
        OnPropertyChanged(nameof(IsShortTextReadingActive));
        OnPropertyChanged(nameof(IsShortTextReadingResting));
        OnPropertyChanged(nameof(IsShortTextReadingPromptVisible));
        OnPropertyChanged(nameof(ShowShortTextReadingStartAction));
        OnPropertyChanged(nameof(IsEmotionQuestionWaiting));
        OnPropertyChanged(nameof(IsEmotionQuestionAnswering));
        OnPropertyChanged(nameof(IsEmotionQuestionResting));
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
        OnPropertyChanged(nameof(IsFallbackStage));
        OnPropertyChanged(nameof(IsCompletionStage));
        OnPropertyChanged(nameof(IsGenericFallbackStage));
        OnPropertyChanged(nameof(ShowDemoPlayAction));
        OnPropertyChanged(nameof(ShowFaceCheckAction));
        OnPropertyChanged(nameof(ShowModuleStartAction));
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
        OnPropertyChanged(nameof(EmotionQuestionRestText));
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

    public void UpdateState(int currentIndex, string completedText, string currentText, string pendingText)
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
            StatusText = currentText;
            DotBrush = CurrentBrush;
            TitleBrush = CurrentTextBrush;
            StatusBrush = CurrentBrush;
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

public sealed class QuestionnaireQuestionItem : ObservableObject
{
    private string answerText = string.Empty;
    private string placeholderText;

    public QuestionnaireQuestionItem(
        int number,
        string text,
        string placeholderText,
        IReadOnlyList<string> answerOptions,
        int optionColumnCount = 1)
    {
        Number = number;
        Text = text;
        AnswerOptions = answerOptions;
        OptionColumnCount = Math.Clamp(optionColumnCount, 1, 2);
        OptionItems = new ObservableCollection<QuestionnaireAnswerOptionItem>(
            answerOptions.Select(static option => new QuestionnaireAnswerOptionItem(option)));
        this.placeholderText = placeholderText;
    }

    public int Number { get; }

    public string Text { get; }

    public IReadOnlyList<string> AnswerOptions { get; }

    public int OptionColumnCount { get; }

    /// <summary>
    /// 普通问卷选项区域宽度。单列按最长选项预留宽度，G/J 两列使用更宽区域保证一行两项舒展。
    /// </summary>
    public double OptionPanelWidth => OptionColumnCount == 1 ? 640 : 820;

    public ObservableCollection<QuestionnaireAnswerOptionItem> OptionItems { get; }

    /// <summary>
    /// 问卷题目根据字数自动调整字号，避免长题溢出、短题在大屏下过小。
    /// </summary>
    public double QuestionFontSize
    {
        get
        {
            var length = Text.Length;
            if (length <= 20)
            {
                return 26;
            }

            return length <= 35 ? 22 : 20;
        }
    }

    public double QuestionLineHeight => QuestionFontSize * 1.5;

    /// <summary>
    /// 是否为 0-10 评分题。评分题使用横向滑条展示，避免十一项纵向按钮超出显示区域。
    /// </summary>
    public bool IsZeroToTenQuestion => AnswerOptions.Count == 11
        && AnswerOptions.Select(static (option, index) => (option, index))
            .All(static item => string.Equals(item.option, item.index.ToString(), StringComparison.Ordinal));

    public string AnswerText
    {
        get => answerText;
        set
        {
            if (SetProperty(ref answerText, value))
            {
                RefreshOptionSelection();
                OnPropertyChanged(nameof(AnswerDisplayText));
                OnPropertyChanged(nameof(AnswerIndex));
                OnPropertyChanged(nameof(SelectionIndex));
                OnPropertyChanged(nameof(Score));
                OnPropertyChanged(nameof(ScoreValue));
            }
        }
    }

    public double ScoreValue
    {
        get => int.TryParse(AnswerText, out var numericScore) ? numericScore : 0;
        set
        {
            var roundedScore = Math.Clamp((int)Math.Round(value), 0, 10);
            AnswerText = roundedScore.ToString();
        }
    }

    public int AnswerIndex
    {
        get
        {
            for (var index = 0; index < AnswerOptions.Count; index++)
            {
                if (string.Equals(AnswerOptions[index], AnswerText, StringComparison.Ordinal))
                {
                    return index + 1;
                }
            }

            return 0;
        }
    }

    public int SelectionIndex
    {
        get => AnswerIndex - 1;
        set
        {
            if (value < 0 || value >= AnswerOptions.Count)
            {
                AnswerText = string.Empty;
                return;
            }

            AnswerText = AnswerOptions[value];
        }
    }

    public int Score => int.TryParse(AnswerText, out var numericScore)
        ? numericScore
        : AnswerIndex;

    public string AnswerDisplayText => string.IsNullOrWhiteSpace(AnswerText)
        ? placeholderText
        : AnswerText;

    public void UpdatePlaceholder(string value)
    {
        placeholderText = value;
        OnPropertyChanged(nameof(AnswerDisplayText));
    }

    /// <summary>
    /// 普通选择题使用按钮渲染，这里同步答案与按钮高亮状态，避免题目切换后残留上一题高亮。
    /// </summary>
    private void RefreshOptionSelection()
    {
        foreach (var option in OptionItems)
        {
            option.IsSelected = string.Equals(option.Text, AnswerText, StringComparison.Ordinal);
        }
    }
}

public sealed class QuestionnaireAnswerOptionItem : ObservableObject
{
    private bool isSelected;

    public QuestionnaireAnswerOptionItem(string text)
    {
        Text = text;
    }

    public string Text { get; }

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }
}
