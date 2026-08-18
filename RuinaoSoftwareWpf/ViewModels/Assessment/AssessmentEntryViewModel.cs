namespace RuinaoSoftwareWpf;

using System.Windows.Input;
using RuinaoSoftwareWpf.ApplicationContracts;

/// <summary>
/// 数字表型评估入口。这里只解析患者级 Run 状态，不承载任何采集模块业务。
/// </summary>
public sealed class AssessmentEntryViewModel : ObservableObject
{
    private readonly IAssessmentRunCoordinator runCoordinator;
    private readonly IPatientService patientService;
    private readonly ILocalizationService localization;
    private readonly ILoggingService logger;
    private readonly AsyncRelayCommand primaryActionCommand;
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
        localization.LanguageChanged += (_, _) => NotifyTextChanged();
        patientService.CurrentPatientChanged += (_, _) => NotifyPatientChanged();
    }

    public event EventHandler<AssessmentRunContext>? RunActivated;

    public ICommand PrimaryActionCommand { get; }

    public AssessmentEntryState State
    {
        get => state;
        private set
        {
            if (SetProperty(ref state, value))
            {
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(IsPrimaryActionVisible));
                OnPropertyChanged(nameof(PrimaryActionText));
                OnPropertyChanged(nameof(HasError));
                primaryActionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string PageTitleText => localization.Text("AssessmentEntryTitle");

    public string CurrentPatientLabelText => localization.Text("AssessmentEntryCurrentPatient");

    public string CurrentPatientName => patientService.CurrentPatient?.Name ?? "--";

    public string CurrentPatientCode => patientService.CurrentPatient?.PatientCode ?? "--";

    public bool IsBusy => State is AssessmentEntryState.Loading
        or AssessmentEntryState.Starting
        or AssessmentEntryState.Recovering;

    public bool IsPrimaryActionVisible => patientService.CurrentPatient is not null;

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
            ErrorMessage = localization.Text("AssessmentEntryNoPatient");
            State = AssessmentEntryState.Error;
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
    }

    private void NotifyTextChanged()
    {
        OnPropertyChanged(nameof(PageTitleText));
        OnPropertyChanged(nameof(CurrentPatientLabelText));
        OnPropertyChanged(nameof(PrimaryActionText));
    }
}
