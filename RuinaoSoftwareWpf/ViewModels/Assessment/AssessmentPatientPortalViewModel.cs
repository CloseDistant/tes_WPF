namespace RuinaoSoftwareWpf;

using System.Collections.ObjectModel;
using System.Windows.Input;

/// <summary>
/// 患者视角的手机号入口。与管理端分页匹配页分离，只接受完整手机号并做精确相等匹配。
/// </summary>
public sealed class AssessmentPatientPortalViewModel : ObservableObject
{
    private readonly IExternalFollowUpService externalFollowUpService;
    private readonly IPatientService patientService;
    private readonly ILocalizationService localization;
    private readonly ILoggingService logger;
    private readonly IToastService toastService;
    private readonly AsyncRelayCommand searchCommand;
    private readonly AsyncRelayCommand selectFollowUpCommand;
    private readonly RelayCommand backCommand;
    private string phoneQuery = string.Empty;
    private string errorMessage = string.Empty;
    private bool isBusy;
    private bool hasSearched;
    private ExternalFollowUpPatient? patient;

    public AssessmentPatientPortalViewModel(
        IExternalFollowUpService externalFollowUpService,
        IPatientService patientService,
        ILocalizationService localization,
        ILoggingService logger,
        IToastService toastService)
    {
        this.externalFollowUpService = externalFollowUpService;
        this.patientService = patientService;
        this.localization = localization;
        this.logger = logger;
        this.toastService = toastService;

        searchCommand = new AsyncRelayCommand(SearchAsync, () => !IsBusy, HandleError);
        selectFollowUpCommand = new AsyncRelayCommand(
            SelectFollowUpAsync,
            parameter => !IsBusy
                && parameter is ExternalFollowUpDetail detail
                && CanSelectFollowUp(detail),
            HandleError);
        backCommand = new RelayCommand(_ => BackRequested?.Invoke(this, EventArgs.Empty));
        SearchCommand = searchCommand;
        SelectFollowUpCommand = selectFollowUpCommand;
        BackCommand = backCommand;
        localization.LanguageChanged += (_, _) => NotifyTextChanged();
    }

    public event EventHandler? BackRequested;

    public event EventHandler<ExternalFollowUpDetail>? FollowUpSelected;

    public ObservableCollection<ExternalFollowUpDetail> FollowUps { get; } = [];

    public ICommand SearchCommand { get; }

    public ICommand SelectFollowUpCommand { get; }

    public ICommand BackCommand { get; }

    public string PageTitleText => localization.Text("AssessmentPatientPortalTitle");

    public string PhoneLabelText => localization.Text("AssessmentPatientPortalPhoneLabel");

    public string PhoneHintText => localization.Text("AssessmentPatientPortalPhoneHint");

    public string SearchActionText => IsBusy
        ? localization.Text("AssessmentPatientPortalSearching")
        : localization.Text("AssessmentPatientPortalSearch");

    public string BackActionText => localization.Text("Back");

    public string EmptyResultText => localization.Text("AssessmentPatientPortalEmpty");

    public string FollowUpTitleText => localization.Text("AssessmentPatientPortalFollowUpTitle");

    public string SelectFollowUpText => localization.Text("AssessmentPatientPortalSelectFollowUp");

    public string PatientNameText => Patient is null
        ? string.Empty
        : string.Format(localization.Text("AssessmentPatientPortalPatientName"), Patient.Name);

    public string PatientPhoneText => Patient is null
        ? string.Empty
        : string.Format(localization.Text("AssessmentPatientPortalPatientPhone"), Patient.Phone);

    public string PatientSummaryText => patient is null
        ? string.Empty
        : $"{patient.Name}  ·  {patient.Phone}";

    public string PhoneQuery
    {
        get => phoneQuery;
        set => SetProperty(
            ref phoneQuery,
            PhoneQueryInputPolicy.Normalize(value, PhoneQueryInputPolicy.PortalMaximumLength));
    }

    public ExternalFollowUpPatient? Patient
    {
        get => patient;
        private set
        {
            if (SetProperty(ref patient, value))
            {
                OnPropertyChanged(nameof(HasPatient));
                OnPropertyChanged(nameof(PatientSummaryText));
                OnPropertyChanged(nameof(PatientNameText));
                OnPropertyChanged(nameof(PatientPhoneText));
                OnPropertyChanged(nameof(ShowEmptyResult));
            }
        }
    }

    public bool HasPatient => Patient is not null;

    public bool ShowEmptyResult => HasSearched && !HasPatient && !IsBusy && !HasError;

    public bool HasSearched
    {
        get => hasSearched;
        private set
        {
            if (SetProperty(ref hasSearched, value))
            {
                OnPropertyChanged(nameof(ShowEmptyResult));
            }
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                OnPropertyChanged(nameof(SearchActionText));
                OnPropertyChanged(nameof(ShowEmptyResult));
                searchCommand.RaiseCanExecuteChanged();
                selectFollowUpCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string ErrorMessage
    {
        get => errorMessage;
        private set
        {
            if (SetProperty(ref errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
                OnPropertyChanged(nameof(ShowEmptyResult));
            }
        }
    }

    public void Reset()
    {
        PhoneQuery = string.Empty;
        Patient = null;
        FollowUps.Clear();
        ErrorMessage = string.Empty;
        HasSearched = false;
    }

    public async Task SearchAsync(CancellationToken cancellationToken = default)
    {
        var phone = PhoneQuery.Trim();
        if (phone.Length == 0)
        {
            ErrorMessage = localization.Text("AssessmentPatientPortalPhoneRequired");
            HasSearched = true;
            return;
        }

        ErrorMessage = string.Empty;
        Patient = null;
        FollowUps.Clear();
        IsBusy = true;
        try
        {
            var matches = await FindExactPatientsAsync(phone, cancellationToken);
            Patient = matches.FirstOrDefault();
            HasSearched = true;
            if (Patient is null)
            {
                toastService.ShowInformation(EmptyResultText, "查询完成");
                return;
            }

            var details = await externalFollowUpService.GetFollowUpDetailsAsync(
                Patient.Phone.Trim(), cancellationToken);
            foreach (var detail in details)
            {
                FollowUps.Add(detail);
            }

            toastService.ShowSuccess("患者已找到", "查询完成");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            HandleError(exception);
            HasSearched = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<IReadOnlyList<ExternalFollowUpPatient>> FindExactPatientsAsync(
        string phone,
        CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        const int maxPages = 100;
        var matches = new List<ExternalFollowUpPatient>();
        var firstPage = await externalFollowUpService.SearchPatientsAsync(
            phone,
            1,
            pageSize,
            cancellationToken);
        AddExactMatches(firstPage.Items, phone, matches);

        var totalPages = firstPage.TotalPage > 0
            ? Math.Min(firstPage.TotalPage, maxPages)
            : 1;
        for (var page = 2; page <= totalPages; page++)
        {
            var result = await externalFollowUpService.SearchPatientsAsync(
                phone,
                page,
                pageSize,
                cancellationToken);
            AddExactMatches(result.Items, phone, matches);
            if (result.Items.Count == 0)
            {
                break;
            }
        }

        return matches;
    }

    private static void AddExactMatches(
        IEnumerable<ExternalFollowUpPatient> patients,
        string phone,
        ICollection<ExternalFollowUpPatient> matches)
    {
        foreach (var item in patients)
        {
            if (string.Equals(item.Phone.Trim(), phone, StringComparison.Ordinal)
                && !matches.Any(existing => string.Equals(existing.PatientId, item.PatientId, StringComparison.Ordinal)))
            {
                matches.Add(item);
            }
        }
    }

    private async Task SelectFollowUpAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (parameter is not ExternalFollowUpDetail detail
            || detail.Id is not long detailId
            || !CanSelectFollowUp(detail)
            || Patient is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var phone = Patient.Phone.Trim();
            var existing = await FindLocalPatientByPhoneAsync(phone, cancellationToken);
            if (existing is null)
            {
                await patientService.CreatePatientAsync(new PatientSaveRequest(
                    null,
                    Patient.Name,
                    PatientSex.Unknown,
                    DateOnly.MinValue,
                    null,
                    phone,
                    null,
                    null,
                    null,
                    null), cancellationToken);
            }
            else
            {
                var updated = await patientService.UpdatePatientAsync(new PatientSaveRequest(
                    existing.PatientCode,
                    Patient.Name,
                    existing.Sex,
                    existing.BirthDate,
                    existing.IdCardNumber,
                    existing.Phone,
                    existing.EmergencyContactName,
                    existing.EmergencyContactPhone,
                    existing.HomeAddress,
                    existing.ClinicalInfo), cancellationToken);
                await patientService.SwitchCurrentPatientAsync(updated.PatientCode, cancellationToken);
            }

            toastService.ShowSuccess("患者已匹配", $"已使用患者：{Patient.Name}");
            FollowUpSelected?.Invoke(this, detail);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            HandleError(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<PatientRecord?> FindLocalPatientByPhoneAsync(
        string phone,
        CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        var offset = 0;
        while (true)
        {
            var page = await patientService.GetPatientsPageAsync(
                new PageRequest(offset, pageSize), cancellationToken);
            var match = page.Items.FirstOrDefault(item =>
                string.Equals(item.Phone?.Trim(), phone, StringComparison.Ordinal));
            if (match is not null || !page.HasMore || page.Items.Count == 0)
            {
                return match;
            }

            offset += page.Items.Count;
        }
    }

    private void HandleError(Exception exception)
    {
        logger.Error("患者端手机号查询或匹配失败", exception);
        ErrorMessage = exception.Message;
        toastService.ShowError("操作失败", exception.Message);
    }

    private static bool CanSelectFollowUp(ExternalFollowUpDetail detail) =>
        detail.Id.HasValue
        && string.Equals(detail.FlowStatusName, "待测评", StringComparison.OrdinalIgnoreCase);

    private void NotifyTextChanged()
    {
        OnPropertyChanged(nameof(PageTitleText));
        OnPropertyChanged(nameof(PhoneLabelText));
        OnPropertyChanged(nameof(PhoneHintText));
        OnPropertyChanged(nameof(SearchActionText));
        OnPropertyChanged(nameof(BackActionText));
        OnPropertyChanged(nameof(EmptyResultText));
        OnPropertyChanged(nameof(FollowUpTitleText));
        OnPropertyChanged(nameof(SelectFollowUpText));
        OnPropertyChanged(nameof(PatientNameText));
        OnPropertyChanged(nameof(PatientPhoneText));
    }
}
