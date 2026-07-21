namespace RuinaoSoftwareWpf;

using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;

public sealed class AuditTrailViewModel : ObservableObject
{
    private readonly IAuditTrailAdministrationService administrationService;
    private readonly IAccountService accountService;
    private readonly ILoggingService logger;
    private readonly AsyncRelayCommand queryCommand;
    private readonly AsyncRelayCommand previousPageCommand;
    private readonly AsyncRelayCommand nextPageCommand;
    private DateOnly startDate;
    private DateOnly endDate;
    private AuditCategoryOption? selectedCategory;
    private AuditActorOption? selectedActor;
    private string statusText = string.Empty;
    private int pageNumber = 1;
    private int pageCount = 1;
    private long totalCount;
    private bool isBusy;

    public AuditTrailViewModel(
        IAuditTrailAdministrationService administrationService,
        IAccountService accountService,
        ILoggingService logger)
    {
        this.administrationService = administrationService;
        this.accountService = accountService;
        this.logger = logger;

        CategoryOptions = new ObservableCollection<AuditCategoryOption>
        {
            new(null, "全部事件"),
            new(AuditEventCategory.IdentitySession, "身份与会话"),
            new(AuditEventCategory.AccountAuthorization, "账号与权限"),
            new(AuditEventCategory.PatientManagement, "患者管理"),
            new(AuditEventCategory.PrescriptionManagement, "处方管理"),
            new(AuditEventCategory.StimulationDevice, "电刺激与设备"),
            new(AuditEventCategory.SecurityConfiguration, "安全配置"),
            new(AuditEventCategory.DataExport, "数据与导出"),
            new(AuditEventCategory.IntegrityCheck, "完整性校验")
        };
        SelectedCategory = CategoryOptions[0];
        ActorOptions = [];
        Events = [];

        queryCommand = new AsyncRelayCommand(
            cancellationToken => QueryAsync(1, cancellationToken),
            () => !IsBusy,
            HandleError);
        previousPageCommand = new AsyncRelayCommand(
            cancellationToken => QueryAsync(PageNumber - 1, cancellationToken),
            () => !IsBusy && PageNumber > 1,
            HandleError);
        nextPageCommand = new AsyncRelayCommand(
            cancellationToken => QueryAsync(PageNumber + 1, cancellationToken),
            () => !IsBusy && PageNumber < PageCount,
            HandleError);

        QueryCommand = queryCommand;
        PreviousPageCommand = previousPageCommand;
        NextPageCommand = nextPageCommand;
    }

    public ObservableCollection<AuditEventRowViewModel> Events { get; }

    public ObservableCollection<AuditCategoryOption> CategoryOptions { get; }

    public ObservableCollection<AuditActorOption> ActorOptions { get; }

    public ICommand QueryCommand { get; }

    public ICommand PreviousPageCommand { get; }

    public ICommand NextPageCommand { get; }

    public DateOnly StartDate
    {
        get => startDate;
        set => SetProperty(ref startDate, value);
    }

    public DateOnly EndDate
    {
        get => endDate;
        set => SetProperty(ref endDate, value);
    }

    public AuditCategoryOption? SelectedCategory
    {
        get => selectedCategory;
        set => SetProperty(ref selectedCategory, value);
    }

    public AuditActorOption? SelectedActor
    {
        get => selectedActor;
        set => SetProperty(ref selectedActor, value);
    }

    public bool IsActorFilterEnabled => accountService.IsCurrentUserAdmin();

    public string StatusText
    {
        get => statusText;
        private set => SetProperty(ref statusText, value);
    }

    public int PageNumber
    {
        get => pageNumber;
        private set
        {
            if (SetProperty(ref pageNumber, value))
            {
                OnPropertyChanged(nameof(PageSummary));
                RaisePagingCanExecuteChanged();
            }
        }
    }

    public int PageCount
    {
        get => pageCount;
        private set
        {
            if (SetProperty(ref pageCount, value))
            {
                OnPropertyChanged(nameof(PageSummary));
                RaisePagingCanExecuteChanged();
            }
        }
    }

    public long TotalCount
    {
        get => totalCount;
        private set
        {
            if (SetProperty(ref totalCount, value))
            {
                OnPropertyChanged(nameof(CountSummary));
            }
        }
    }

    public string CountSummary => $"共{TotalCount:N0}条";

    public string PageSummary => $"第{PageNumber}/{PageCount}页";

    public bool IsBusy
    {
        get => isBusy;
        private set
        {
            if (SetProperty(ref isBusy, value))
            {
                queryCommand.RaiseCanExecuteChanged();
                RaisePagingCanExecuteChanged();
            }
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        StartDate = today;
        EndDate = today;
        await LoadActorOptionsAsync(cancellationToken);
        await QueryAsync(1, cancellationToken);
    }

    public async Task<AuditExportResult> ExportAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        StatusText = "正在导出安全审计记录...";
        try
        {
            var result = await administrationService.ExportCsvAsync(BuildQuery(1), filePath, cancellationToken);
            StatusText = $"已导出{result.ExportedCount:N0}条，SHA-256：{result.Sha256[..12]}...";
            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task QueryAsync(int requestedPage, CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        StatusText = "正在查询...";
        try
        {
            var result = await administrationService.QueryAsync(BuildQuery(Math.Max(1, requestedPage)), cancellationToken);
            Events.Clear();
            foreach (var item in result.Items)
            {
                Events.Add(new AuditEventRowViewModel(item));
            }

            TotalCount = result.TotalCount;
            PageCount = Math.Max(1, (int)Math.Ceiling(result.TotalCount / (double)result.PageSize));
            PageNumber = Math.Min(result.PageNumber, PageCount);
            StatusText = result.Items.Count == 0 ? "当前条件下没有审计记录" : string.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private AuditQuery BuildQuery(int requestedPage)
    {
        if (StartDate == default || EndDate == default)
        {
            throw new InvalidOperationException("请选择开始日期和结束日期");
        }

        if (EndDate < StartDate)
        {
            throw new InvalidOperationException("结束日期不能早于开始日期");
        }

        var (startUtc, endUtc) = BuildUtcDateRange(StartDate, EndDate);
        return new AuditQuery(startUtc, endUtc, SelectedCategory?.Value, SelectedActor?.LoginName, requestedPage, 50);
    }

    internal static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) BuildUtcDateRange(
        DateOnly startDate,
        DateOnly endDate)
    {
        var startLocal = startDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var endLocal = endDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Unspecified);
        return (
            new DateTimeOffset(startLocal, TimeZoneInfo.Local.GetUtcOffset(startLocal)).ToUniversalTime(),
            new DateTimeOffset(endLocal, TimeZoneInfo.Local.GetUtcOffset(endLocal)).ToUniversalTime());
    }

    private async Task LoadActorOptionsAsync(CancellationToken cancellationToken)
    {
        var loginNames = await administrationService.GetActorLoginNamesAsync(cancellationToken);
        ActorOptions.Clear();
        if (accountService.IsCurrentUserAdmin())
        {
            ActorOptions.Add(new AuditActorOption(null, "全部操作者"));
        }

        foreach (var loginName in loginNames)
        {
            ActorOptions.Add(new AuditActorOption(loginName, loginName));
        }

        SelectedActor = accountService.IsCurrentUserAdmin()
            ? ActorOptions.FirstOrDefault()
            : ActorOptions.FirstOrDefault(item => string.Equals(
                item.LoginName,
                accountService.CurrentUser?.LoginName,
                StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(IsActorFilterEnabled));
    }

    private void HandleError(Exception exception)
    {
        logger.Error("安全审计界面操作失败", exception);
        StatusText = exception.Message;
        IsBusy = false;
    }

    private void RaisePagingCanExecuteChanged()
    {
        previousPageCommand.RaiseCanExecuteChanged();
        nextPageCommand.RaiseCanExecuteChanged();
    }
}

public sealed class AuditEventRowViewModel
{
    public AuditEventRowViewModel(AuditEventRecord record)
    {
        TimeText = record.OccurredAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        ActorText = record.ActorLoginName;
        RoleText = AuditDisplayNames.Role(record.ActorRoleId);
        CategoryText = AuditDisplayNames.Category(record.Category);
        ActionText = AuditDisplayNames.Action(record.ActionCode);
        ResultText = AuditDisplayNames.Result(record.Result);
        ResultForeground = record.Result switch
        {
            AuditEventResult.Success => "#73D995",
            AuditEventResult.Blocked => "#E7BE6D",
            _ => "#F29696"
        };
        ResultBackground = record.Result switch
        {
            AuditEventResult.Success => "#20372B",
            AuditEventResult.Blocked => "#3A3022",
            _ => "#3A2529"
        };
        ReasonText = record.Reason ?? record.FailureCode ?? string.Empty;
    }

    public string TimeText { get; }
    public string ActorText { get; }
    public string RoleText { get; }
    public string CategoryText { get; }
    public string ActionText { get; }
    public string ResultText { get; }
    public string ResultForeground { get; }
    public string ResultBackground { get; }
    public string ReasonText { get; }
}
