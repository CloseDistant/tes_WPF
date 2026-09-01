namespace RuinaoSoftwareWpf;

using System.Windows.Input;
using RuinaoSoftwareWpf.ApplicationContracts;

/// <summary>
/// 数字表型采集入口。这里只解析患者级 Run 状态，不承载任何采集模块业务。
/// </summary>
public sealed class AssessmentEntryViewModel : ObservableObject
{
    private readonly IAssessmentRunCoordinator runCoordinator;
    private readonly IPatientService patientService;
    private readonly ILocalizationService localization;
    private readonly ILoggingService logger;
    private readonly AsyncRelayCommand primaryActionCommand;
    private readonly AsyncRelayCommand selectPatientCommand;
    private AssessmentEntryState state = AssessmentEntryState.Loading;
    private AssessmentRunContext? activeRun;
    private string errorMessage = string.Empty;

    public AssessmentEntryViewModel(
        IAssessmentRunCoordinator runCoordinator,
        IPatientService patientService,
        ILocalizationService localization,
        ILoggingService logger)
    {
        this.runCoordinator = runCoordinator;
        this.patientService = patientService;
        this.localization = localization;
        this.logger = logger;
        primaryActionCommand = new AsyncRelayCommand(
            ExecutePrimaryActionAsync,
            () => State is AssessmentEntryState.NoActiveRun
                or AssessmentEntryState.ActiveRun
                or AssessmentEntryState.Error);
        PrimaryActionCommand = primaryActionCommand;
        selectPatientCommand = new AsyncRelayCommand(
            ExecuteSelectPatientAsync,
            () => patientService.CurrentPatient is null
                && State is AssessmentEntryState.NoPatient or AssessmentEntryState.Error);
        SelectPatientCommand = selectPatientCommand;
        localization.LanguageChanged += (_, _) => NotifyTextChanged();
        patientService.CurrentPatientChanged += (_, _) => NotifyPatientChanged();
    }

    public event EventHandler<AssessmentRunContext>? RunActivated;

    public event EventHandler<AssessmentPatientSelectionRequestedEventArgs>? PatientSelectionRequested;

    public ICommand PrimaryActionCommand { get; }

    public ICommand SelectPatientCommand { get; }

    public AssessmentEntryState State
    {
        get => state;
        private set
        {
            if (SetProperty(ref state, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(IsPrimaryActionVisible));
                OnPropertyChanged(nameof(IsNoPatientState));
                OnPropertyChanged(nameof(PrimaryActionText));
                OnPropertyChanged(nameof(SelectPatientActionText));
                OnPropertyChanged(nameof(HasError));
                primaryActionCommand.RaiseCanExecuteChanged();
                selectPatientCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string PageTitleText => localization.Text("AssessmentEntryTitle");

    public string CurrentPatientLabelText => localization.Text("AssessmentEntryCurrentPatient");

    public string CurrentPatientName => patientService.CurrentPatient?.Name ?? "--";

    public string CurrentPatientCode => patientService.CurrentPatient?.PatientCode ?? "--";

    public string NoPatientTitleText => localization.Text("AssessmentEntryNoPatientTitle");

    public string NoPatientDescriptionText => localization.Text("AssessmentEntryNoPatientDescription");

    public bool IsBusy => State is AssessmentEntryState.Loading
        or AssessmentEntryState.Starting
        or AssessmentEntryState.Recovering
        or AssessmentEntryState.SelectingPatient;

    public bool IsNoPatientState => patientService.CurrentPatient is null;

    public bool IsPrimaryActionVisible => patientService.CurrentPatient is not null;

    public string SelectPatientActionText => State == AssessmentEntryState.SelectingPatient
        ? localization.Text("AssessmentEntrySelectingPatient")
        : localization.Text("AssessmentEntrySelectPatient");

    public string PrimaryActionText => State switch
    {
        AssessmentEntryState.Loading => localization.Text("AssessmentEntryLoading"),
        AssessmentEntryState.ActiveRun => localization.Text("AssessmentEntryContinue"),
        AssessmentEntryState.Recovering => localization.Text("AssessmentEntryRecovering"),
        AssessmentEntryState.Starting => localization.Text("AssessmentEntryStarting"),
        AssessmentEntryState.Error => localization.Text("AssessmentEntryReload"),
        _ => localization.Text("AssessmentEntryStartNew")
    };

    public bool HasError => State == AssessmentEntryState.Error;

    public string ErrorMessage
    {
        get => errorMessage;
        private set => SetProperty(ref errorMessage, value);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        State = AssessmentEntryState.Loading;
        ErrorMessage = string.Empty;
        activeRun = null;
        NotifyPatientChanged();
        if (patientService.CurrentPatient is null)
        {
            State = AssessmentEntryState.NoPatient;
            return;
        }

        try
        {
            activeRun = await runCoordinator.GetActiveRunAsync(
                AssessmentCaptureViewModel.TotalFormalModuleCount,
                cancellationToken);
            State = activeRun is null
                ? AssessmentEntryState.NoActiveRun
                : AssessmentEntryState.ActiveRun;
        }
        catch (Exception exception)
        {
            ApplyError(localization.Text("AssessmentEntryLoadFailed"), exception);
        }
    }

    public async Task ExecuteSelectPatientAsync(CancellationToken cancellationToken = default)
    {
        if (patientService.CurrentPatient is not null
            || State is not (AssessmentEntryState.NoPatient or AssessmentEntryState.Error))
        {
            return;
        }

        State = AssessmentEntryState.SelectingPatient;
        ErrorMessage = string.Empty;
        try
        {
            var request = new AssessmentPatientSelectionRequestedEventArgs(cancellationToken);
            PatientSelectionRequested?.Invoke(this, request);
            if (!request.IsHandled)
            {
                throw new InvalidOperationException("患者选择入口尚未连接到主界面。");
            }

            await request.Completion;
            await LoadAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            State = AssessmentEntryState.NoPatient;
        }
        catch (Exception exception)
        {
            ApplyError(localization.Text("AssessmentEntryPatientSelectionFailed"), exception);
        }
    }

    public async Task ExecutePrimaryActionAsync(CancellationToken cancellationToken = default)
    {
        if (State == AssessmentEntryState.Error)
        {
            await LoadAsync(cancellationToken);
            return;
        }

        try
        {
            AssessmentRunContext run;
            if (activeRun is null)
            {
                State = AssessmentEntryState.Starting;
                run = await runCoordinator.CreateRunAsync(
                    AssessmentCaptureViewModel.TotalFormalModuleCount,
                    cancellationToken);
            }
            else
            {
                State = AssessmentEntryState.Recovering;
                run = await runCoordinator.ResumeRunAsync(
                    activeRun.RunId,
                    AssessmentCaptureViewModel.TotalFormalModuleCount,
                    cancellationToken);
            }

            activeRun = run;
            State = AssessmentEntryState.ActiveRun;
            RunActivated?.Invoke(this, run);
        }
        catch (Exception exception)
        {
            ApplyError(localization.Text("AssessmentEntryActionFailed"), exception);
        }
    }

    private void ApplyError(string prefix, Exception exception)
    {
        logger.Error(prefix, exception);
        ErrorMessage = $"{prefix}：{exception.Message}";
        State = AssessmentEntryState.Error;
    }

    private void NotifyPatientChanged()
    {
        OnPropertyChanged(nameof(CurrentPatientName));
        OnPropertyChanged(nameof(CurrentPatientCode));
        OnPropertyChanged(nameof(IsPrimaryActionVisible));
        OnPropertyChanged(nameof(IsNoPatientState));
        selectPatientCommand.RaiseCanExecuteChanged();
    }

    private void NotifyTextChanged()
    {
        OnPropertyChanged(nameof(PageTitleText));
        OnPropertyChanged(nameof(CurrentPatientLabelText));
        OnPropertyChanged(nameof(PrimaryActionText));
        OnPropertyChanged(nameof(NoPatientTitleText));
        OnPropertyChanged(nameof(NoPatientDescriptionText));
        OnPropertyChanged(nameof(SelectPatientActionText));
    }
}
