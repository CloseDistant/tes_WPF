namespace RuinaoSoftwareWpf;

using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

public sealed class ReportViewModel : ObservableObject
{
    private const int PageSize = 20;
    private readonly IStimulationRecordService stimulationRecordService;
    private readonly IUserDialogService dialogService;
    private readonly IAccountService accountService;
    private readonly IAuthorizationService authorizationService;
    private readonly IAuditTrailService auditTrail;
    private readonly IAuditLogService auditLog;
    private StimulationRecordRow? selectedRecord;
    private int currentPage = 1;
    private int totalCount;
    private bool isLoading;

    public ReportViewModel(
        IStimulationRecordService stimulationRecordService,
        IUserDialogService dialogService,
        IAccountService accountService,
        IAuthorizationService authorizationService,
        IAuditTrailService auditTrail,
        IAuditLogService auditLog)
    {
        this.stimulationRecordService = stimulationRecordService;
        this.dialogService = dialogService;
        this.accountService = accountService;
        this.authorizationService = authorizationService;
        this.auditTrail = auditTrail;
        this.auditLog = auditLog;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, onError: ex =>
            ShowOperationError("记录", ex));
        ExportCommand = new AsyncRelayCommand(
            ExportAsync,
            parameter => parameter is StimulationRecordRow || SelectedRecord is not null,
            ex => ShowOperationError("处方记录", ex));
        ReuseCommand = new RelayCommand(
            parameter => Reuse(parameter as StimulationRecordRow ?? SelectedRecord),
            parameter => parameter is StimulationRecordRow || SelectedRecord is not null);
        PreviousPageCommand = new AsyncRelayCommand(
            GoPreviousPageAsync,
            () => CurrentPage > 1,
            ex => ShowOperationError("记录", ex));
        NextPageCommand = new AsyncRelayCommand(
            GoNextPageAsync,
            () => CurrentPage < TotalPages,
            ex => ShowOperationError("记录", ex));
        accountService.CurrentUserChanged += (_, _) => ClearRecords();
    }

    public ObservableCollection<StimulationRecordRow> Records { get; } = [];

    public ICommand RefreshCommand { get; }

    public ICommand ExportCommand { get; }

    public ICommand ReuseCommand { get; }

    public ICommand PreviousPageCommand { get; }

    public ICommand NextPageCommand { get; }

    public int CurrentPage
    {
        get => currentPage;
        private set
        {
            if (!SetProperty(ref currentPage, value))
            {
                return;
            }

            OnPropertyChanged(nameof(PageStatusText));
            RefreshPagingCommandStates();
        }
    }

    public int TotalCount
    {
        get => totalCount;
        private set
        {
            if (!SetProperty(ref totalCount, value))
            {
                return;
            }

            OnPropertyChanged(nameof(TotalPages));
            OnPropertyChanged(nameof(PageStatusText));
            RefreshPagingCommandStates();
        }
    }

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

    public string PageStatusText => $"第 {CurrentPage} / {TotalPages} 页，共 {TotalCount} 条，每页 {PageSize} 条";

    public StimulationRecordRow? SelectedRecord
    {
        get => selectedRecord;
        set
        {
            if (!SetProperty(ref selectedRecord, value))
            {
                return;
            }

            (ExportCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (ReuseCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public event EventHandler<PrescriptionDefinition>? ReuseRequested;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await RefreshAsync(cancellationToken);
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        CurrentPage = 1;
        await LoadPageAsync(CurrentPage, cancellationToken);
    }

    private async Task GoPreviousPageAsync(CancellationToken cancellationToken)
    {
        if (CurrentPage <= 1)
        {
            return;
        }

        await LoadPageAsync(CurrentPage - 1, cancellationToken);
    }

    private async Task GoNextPageAsync(CancellationToken cancellationToken)
    {
        if (CurrentPage >= TotalPages)
        {
            return;
        }

        await LoadPageAsync(CurrentPage + 1, cancellationToken);
    }

    private async Task LoadPageAsync(int pageNumber, CancellationToken cancellationToken = default)
    {
        if (isLoading)
        {
            return;
        }

        isLoading = true;
        try
        {
            pageNumber = Math.Max(1, pageNumber);
            var offset = (pageNumber - 1) * PageSize;
            var page = await stimulationRecordService.GetTreatmentRecordsPageAsync(
                new PageRequest(offset, PageSize),
                cancellationToken);
            TotalCount = page.TotalCount ?? offset + page.Items.Count + (page.HasMore ? 1 : 0);
            CurrentPage = Math.Min(pageNumber, TotalPages);
            var rowNumber = (CurrentPage - 1) * PageSize + 1;
            Records.Clear();
            foreach (var record in page.Items)
            {
                Records.Add(new StimulationRecordRow(rowNumber++, record));
            }

            SelectedRecord = Records.FirstOrDefault();
        }
        finally
        {
            isLoading = false;
            RefreshPagingCommandStates();
        }
    }

    private async Task ExportAsync(object? parameter, CancellationToken cancellationToken)
    {
        authorizationService.RequireSignedIn();
        var row = parameter as StimulationRecordRow ?? SelectedRecord;
        if (row is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "导出处方记录",
            Filter = "CSV 文件 (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = SanitizeFileName(row.ParameterRecord.Name) + ".csv"
        };
        if (dialog.ShowDialog(Application.Current?.MainWindow) != true)
        {
            return;
        }

        var csv = PrescriptionViewModel.BuildCsv(row.ParameterRecord);
        await File.WriteAllTextAsync(dialog.FileName, csv, new UTF8Encoding(true), cancellationToken);
        await auditTrail.AppendAsync(
            new AuditEventInput(
                AuditEventCategory.DataExport,
                "EXPORT_TREATMENT_RECORD_CSV",
                AuditActor.From(accountService.CurrentUser),
                "TreatmentRecord",
                row.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                AuditEventResult.Success,
                Reason: $"file={Path.GetFileName(dialog.FileName)}"),
            cancellationToken);
        dialogService.ShowInformation("处方记录", $"处方记录已保存到：\n{dialog.FileName}");
    }

    private void Reuse(StimulationRecordRow? row)
    {
        authorizationService.RequireSignedIn();
        if (row is null)
        {
            return;
        }

        auditLog.RecordUserAction("Reuse treatment record");
        ReuseRequested?.Invoke(this, row.ParameterRecord);
    }

    private void ClearRecords()
    {
        Records.Clear();
        SelectedRecord = null;
        CurrentPage = 1;
        TotalCount = 0;
    }

    private void RefreshPagingCommandStates()
    {
        (PreviousPageCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (NextPageCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private void ShowOperationError(string title, Exception exception)
    {
        dialogService.ShowError(title, $"操作失败：{exception.Message}");
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(value) ? "处方记录" : value;
    }
}

public sealed class StimulationRecordRow
{
    public StimulationRecordRow(int rowNumber, StimulationTreatmentRecord record)
    {
        RowNumber = rowNumber;
        Id = record.Id;
        PatientDisplay = record.PatientDisplay;
        StimulationType = record.StimulationType;
        TreatmentDate = record.TreatmentDate.ToString("yyyy-MM-dd");
        PrescriptionName = record.PrescriptionName;
        AdverseReactionRecord = record.AdverseReactionRecord;
        ParameterRecord = record.ParameterRecord;
    }

    public int RowNumber { get; }

    public long Id { get; }

    public string PatientDisplay { get; }

    public string StimulationType { get; }

    public string TreatmentDate { get; }

    public string PrescriptionName { get; }

    public string AdverseReactionRecord { get; }

    public PrescriptionDefinition ParameterRecord { get; }
}
