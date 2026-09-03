namespace RuinaoSoftwareWpf;

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

/// <summary>
/// 患者匹配页面。负责手机号分页查询，以及单行展开的随访详情查询。
/// </summary>
public sealed class AssessmentPatientMatchingViewModel : ObservableObject
{
    private static readonly IReadOnlyList<int> pageSizeOptions = [10, 20, 50];
    private readonly IExternalFollowUpService externalFollowUpService;
    private readonly ILocalizationService localization;
    private readonly ILoggingService logger;
    private readonly IToastService toastService;
    private readonly RelayCommand searchCommand;
    private readonly RelayCommand backCommand;
    private readonly AsyncRelayCommand previousPageCommand;
    private readonly AsyncRelayCommand nextPageCommand;
    private readonly AsyncRelayCommand goToPageCommand;
    private readonly AsyncRelayCommand selectPatientCommand;
    private string phoneQuery = string.Empty;
    private string pageNumberInput = "1";
    private bool isBusy;
    private string errorMessage = string.Empty;
    private long total;
    private int pageNumber = 1;
    private int pageSize = 10;
    private int totalPage;
    private int selectedPageSize = 10;
    private bool isSearchCooldown;
    private CancellationTokenSource? activeSearchCts;
    private CancellationTokenSource? searchCooldownCts;
    private long requestVersion;

    public AssessmentPatientMatchingViewModel(
        IExternalFollowUpService externalFollowUpService,
        ILocalizationService localization,
        ILoggingService logger,
        IToastService toastService)
    {
        this.externalFollowUpService = externalFollowUpService;
        this.localization = localization;
        this.logger = logger;
        this.toastService = toastService;

        searchCommand = new RelayCommand(
            _ => StartSearch(),
            _ => !IsSearchCooldown);
        SearchCommand = searchCommand;

        backCommand = new RelayCommand(_ => BackRequested?.Invoke(this, EventArgs.Empty));
        BackCommand = backCommand;

        previousPageCommand = new AsyncRelayCommand(
            token => GoToPageAsync(PageNumber - 1, token),
            () => CanGoPrevious,
            HandleSearchError);
        PreviousPageCommand = previousPageCommand;

        nextPageCommand = new AsyncRelayCommand(
            token => GoToPageAsync(PageNumber + 1, token),
            () => CanGoNext,
            HandleSearchError);
        NextPageCommand = nextPageCommand;

        goToPageCommand = new AsyncRelayCommand(
            GoToPageFromInputAsync,
            () => !IsBusy && TotalPage > 0,
            HandleSearchError);
        GoToPageCommand = goToPageCommand;

        selectPatientCommand = new AsyncRelayCommand(
            SelectPatientAsync,
            parameter => !IsBusy && parameter is ExternalFollowUpPatientRowViewModel,
            HandleFollowUpError);
        SelectPatientCommand = selectPatientCommand;

        localization.LanguageChanged += (_, _) => NotifyTextChanged();
    }

    public event EventHandler? BackRequested;

    public ObservableCollection<ExternalFollowUpPatientRowViewModel> Patients { get; } = [];

    public ICommand SearchCommand { get; }

    public ICommand BackCommand { get; }

    public ICommand PreviousPageCommand { get; }

    public ICommand NextPageCommand { get; }

    public ICommand GoToPageCommand { get; }

    public ICommand SelectPatientCommand { get; }

    public IReadOnlyList<int> PageSizeOptions => pageSizeOptions;

    public string PageTitleText => localization.Text("AssessmentPatientMatchingTitle");

    public string PhoneLabelText => localization.Text("AssessmentPatientMatchingPhoneLabel");

    public string PhoneQueryHintText => localization.Text("AssessmentPatientMatchingPhoneHint");

    public string SearchActionText => IsSearchCooldown
        ? localization.Text("AssessmentPatientMatchingSearching")
        : localization.Text("AssessmentPatientMatchingSearch");

    public string BackActionText => localization.Text("Back");

    public string EmptyResultText => localization.Text("AssessmentPatientMatchingEmpty");

    public string ResultSummaryText => string.Format(
        localization.Text("AssessmentPatientMatchingResultSummary"),
        Total);

    public string PageSummaryText => string.Format(
        localization.Text("AssessmentPatientMatchingPageSummary"),
        CurrentPage,
        TotalPage,
        PageSize,
        Total);

    public string PreviousPageText => localization.Text("AssessmentPatientMatchingPreviousPage");

    public string NextPageText => localization.Text("AssessmentPatientMatchingNextPage");

    public string GoToPageText => localization.Text("AssessmentPatientMatchingGoToPage");

    public string PageSizeText => localization.Text("AssessmentPatientMatchingPageSize");

    public string FollowUpTitleText => localization.Text("AssessmentPatientMatchingFollowUpTitle");

    public string FollowUpLoadingText => localization.Text("AssessmentPatientMatchingFollowUpLoading");

    public string FollowUpEmptyText => localization.Text("AssessmentPatientMatchingFollowUpEmpty");

    public string PhoneQuery
    {
        get => phoneQuery;
        set => SetProperty(ref phoneQuery, value);
    }

    public string PageNumberInput
    {
        get => pageNumberInput;
        set
        {
            var text = value?.Trim() ?? string.Empty;
            if (!int.TryParse(text, out var requestedPage))
            {
                text = PageNumber.ToString();
            }
            else
            {
                var maxPage = TotalPage > 0 ? TotalPage : 1;
                text = Math.Clamp(requestedPage, 1, maxPage).ToString();
            }

            SetProperty(ref pageNumberInput, text);
        }
    }

    public int SelectedPageSize
    {
        get => selectedPageSize;
        set
        {
            var normalized = pageSizeOptions.Contains(value) ? value : 10;
            SetProperty(ref selectedPageSize, normalized);
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (!SetProperty(ref isBusy, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SearchActionText));
            previousPageCommand.RaiseCanExecuteChanged();
            nextPageCommand.RaiseCanExecuteChanged();
            goToPageCommand.RaiseCanExecuteChanged();
            selectPatientCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsSearchCooldown
    {
        get => isSearchCooldown;
        private set
        {
            if (SetProperty(ref isSearchCooldown, value))
            {
                OnPropertyChanged(nameof(SearchActionText));
                searchCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasResults => Patients.Count > 0;

    public long Total
    {
        get => total;
        private set
        {
            if (SetProperty(ref total, value))
            {
                OnPropertyChanged(nameof(ResultSummaryText));
                OnPropertyChanged(nameof(PageSummaryText));
            }
        }
    }

    public int PageNumber
    {
        get => pageNumber;
        private set
        {
            if (SetProperty(ref pageNumber, value))
            {
                PageNumberInput = value.ToString();
                OnPropertyChanged(nameof(CurrentPage));
                OnPropertyChanged(nameof(CanGoPrevious));
                OnPropertyChanged(nameof(CanGoNext));
                OnPropertyChanged(nameof(PageSummaryText));
                previousPageCommand.RaiseCanExecuteChanged();
                nextPageCommand.RaiseCanExecuteChanged();
                goToPageCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public int PageSize
    {
        get => pageSize;
        private set
        {
            if (SetProperty(ref pageSize, value))
            {
                OnPropertyChanged(nameof(PageSummaryText));
            }
        }
    }

    public int TotalPage
    {
        get => totalPage;
        private set
        {
            if (SetProperty(ref totalPage, value))
            {
                OnPropertyChanged(nameof(CurrentPage));
                OnPropertyChanged(nameof(CanGoPrevious));
                OnPropertyChanged(nameof(CanGoNext));
                OnPropertyChanged(nameof(PageSummaryText));
                previousPageCommand.RaiseCanExecuteChanged();
                nextPageCommand.RaiseCanExecuteChanged();
                goToPageCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public int CurrentPage => TotalPage == 0 ? 0 : Math.Clamp(PageNumber, 1, TotalPage);

    public bool CanGoPrevious => !IsBusy && TotalPage > 0 && PageNumber > 1;

    public bool CanGoNext => !IsBusy && TotalPage > 0 && PageNumber < TotalPage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string ErrorMessage
    {
        get => errorMessage;
        private set
        {
            if (SetProperty(ref errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public async Task SearchAsync(CancellationToken cancellationToken = default)
    {
        BeginSearchCooldown();
        var nextCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var previousCts = Interlocked.Exchange(ref activeSearchCts, nextCts);
        previousCts?.Cancel();
        previousCts?.Dispose();
        toastService.ShowInformation("正在查询患者，请稍候", "查询患者");

        try
        {
            await LoadPageAsync(1, nextCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 被新的查询替代时不提示错误。
        }
        finally
        {
            if (ReferenceEquals(activeSearchCts, nextCts))
            {
                activeSearchCts = null;
            }

            nextCts.Dispose();
        }
    }

    /// <summary>
    /// 按指定页码查询。页码始终限制在接口返回的有效范围内，避免请求超出总页数。
    /// </summary>
    public async Task GoToPageAsync(int requestedPage, CancellationToken cancellationToken = default)
    {
        if (TotalPage == 0)
        {
            PageNumber = 1;
            return;
        }

        var boundedPage = Math.Clamp(requestedPage, 1, TotalPage);
        if (boundedPage == PageNumber && Patients.Count > 0)
        {
            PageNumberInput = boundedPage.ToString();
            return;
        }

        await LoadPageAsync(boundedPage, cancellationToken).ConfigureAwait(false);
    }

    private async Task GoToPageFromInputAsync(CancellationToken cancellationToken)
    {
        if (!int.TryParse(PageNumberInput, out var requestedPage))
        {
            PageNumberInput = PageNumber.ToString();
            return;
        }

        await GoToPageAsync(requestedPage, cancellationToken).ConfigureAwait(false);
    }

    private async Task LoadPageAsync(int requestedPage, CancellationToken cancellationToken)
    {
        var currentVersion = Interlocked.Increment(ref requestVersion);
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var result = await externalFollowUpService.SearchPatientsAsync(
                PhoneQuery,
                requestedPage,
                SelectedPageSize,
                cancellationToken: cancellationToken);

            Patients.Clear();
            foreach (var patient in result.Items)
            {
                Patients.Add(new ExternalFollowUpPatientRowViewModel(patient));
            }

            Total = result.Total;
            PageSize = NormalizePositiveInt(result.PageSize, SelectedPageSize);
            TotalPage = NormalizePageCount(result.TotalPage, result.Total, PageSize);
            PageNumber = TotalPage == 0
                ? 1
                : Math.Clamp(NormalizePositiveInt(result.PageNumber, requestedPage), 1, TotalPage);
            OnPropertyChanged(nameof(HasResults));
            if (result.Total > 0)
            {
                toastService.ShowSuccess("查询完成", "患者列表已更新");
            }
            else
            {
                toastService.ShowInformation("未找到可匹配患者", "查询完成");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            HandleSearchError(exception);
        }
        finally
        {
            if (currentVersion == Volatile.Read(ref requestVersion))
            {
                IsBusy = false;
            }
        }
    }

    private void HandleSearchError(Exception exception)
    {
        logger.Error("外部患者查询失败", exception);
        ErrorMessage = exception.Message;
        if (exception is TimeoutException or TaskCanceledException)
        {
            toastService.ShowError("查询超时", "服务器响应超过 15 秒，请检查网络或 VPN。");
        }
        else
        {
            toastService.ShowError("查询失败", exception.Message);
        }
    }

    private async void StartSearch()
    {
        await SearchAsync().ConfigureAwait(false);
    }

    private void BeginSearchCooldown()
    {
        searchCooldownCts?.Cancel();
        searchCooldownCts?.Dispose();
        var owner = searchCooldownCts = new CancellationTokenSource();
        IsSearchCooldown = true;
        _ = ReleaseSearchCooldownAsync(owner);
    }

    private async Task ReleaseSearchCooldownAsync(CancellationTokenSource owner)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), owner.Token).ConfigureAwait(false);
            if (ReferenceEquals(searchCooldownCts, owner))
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher is not null && !dispatcher.CheckAccess())
                {
                    await dispatcher.InvokeAsync(() => IsSearchCooldown = false);
                }
                else
                {
                    IsSearchCooldown = false;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(searchCooldownCts, owner))
            {
                searchCooldownCts = null;
                owner.Dispose();
            }
        }
    }

    internal async Task SelectPatientAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (parameter is not ExternalFollowUpPatientRowViewModel row)
        {
            return;
        }

        if (row.IsExpanded)
        {
            row.IsExpanded = false;
            return;
        }

        foreach (var patient in Patients)
        {
            patient.IsExpanded = ReferenceEquals(patient, row);
            if (!ReferenceEquals(patient, row))
            {
                patient.IsLoadingFollowUps = false;
                patient.FollowUpError = string.Empty;
            }
        }

        row.ClearFollowUps();
        row.FollowUpError = string.Empty;
        row.IsLoadingFollowUps = true;
        try
        {
            var details = await externalFollowUpService.GetFollowUpDetailsAsync(
                row.Phone,
                cancellationToken);
            row.ReplaceFollowUps(details);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            HandleFollowUpError(exception);
            row.FollowUpError = exception.Message;
        }
        finally
        {
            row.IsLoadingFollowUps = false;
        }
    }

    private void HandleFollowUpError(Exception exception)
    {
        logger.Error("外部随访详情查询失败", exception);
    }

    private static int NormalizePositiveInt(long value, int fallback)
    {
        if (value < 1)
        {
            return fallback;
        }

        return value > int.MaxValue ? int.MaxValue : (int)value;
    }

    private static int NormalizePageCount(long reportedTotalPage, long reportedTotal, int effectivePageSize)
    {
        if (reportedTotalPage > 0)
        {
            return reportedTotalPage > int.MaxValue ? int.MaxValue : (int)reportedTotalPage;
        }

        if (reportedTotal <= 0 || effectivePageSize <= 0)
        {
            return 0;
        }

        var calculated = (reportedTotal + effectivePageSize - 1) / effectivePageSize;
        return calculated > int.MaxValue ? int.MaxValue : (int)calculated;
    }

    private void NotifyTextChanged()
    {
        OnPropertyChanged(nameof(PageTitleText));
        OnPropertyChanged(nameof(PhoneLabelText));
        OnPropertyChanged(nameof(PhoneQueryHintText));
        OnPropertyChanged(nameof(SearchActionText));
        OnPropertyChanged(nameof(BackActionText));
        OnPropertyChanged(nameof(EmptyResultText));
        OnPropertyChanged(nameof(ResultSummaryText));
        OnPropertyChanged(nameof(PageSummaryText));
        OnPropertyChanged(nameof(PreviousPageText));
        OnPropertyChanged(nameof(NextPageText));
        OnPropertyChanged(nameof(GoToPageText));
        OnPropertyChanged(nameof(PageSizeText));
        OnPropertyChanged(nameof(FollowUpTitleText));
        OnPropertyChanged(nameof(FollowUpLoadingText));
        OnPropertyChanged(nameof(FollowUpEmptyText));
    }
}
