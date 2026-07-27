namespace RuinaoSoftwareWpf;

using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using RuinaoSoftwareWpf.Views.Dialogs;

public sealed class PrescriptionViewModel : ObservableObject
{
    private const int PageSize = 30;
    private readonly IPrescriptionService prescriptionService;
    private readonly IUserDialogService dialogService;
    private readonly IFeatureVisibilityService featureVisibilityService;
    private readonly IAccountService accountService;
    private readonly IAuthorizationService authorizationService;
    private readonly IAuditTrailService auditTrail;
    private readonly IAuditLogService auditLog;
    private PrescriptionDefinition? selectedPrescription;
    private bool initialized;
    private int nextOffset;
    private bool hasMore = true;
    private bool isLoading;
    private bool visibilityInvalidated;

    public PrescriptionViewModel(
        IPrescriptionService prescriptionService,
        IUserDialogService dialogService,
        IFeatureVisibilityService featureVisibilityService,
        IAccountService accountService,
        IAuthorizationService authorizationService,
        IAuditTrailService auditTrail,
        IAuditLogService auditLog)
    {
        this.prescriptionService = prescriptionService;
        this.dialogService = dialogService;
        this.featureVisibilityService = featureVisibilityService;
        this.accountService = accountService;
        this.authorizationService = authorizationService;
        this.auditTrail = auditTrail;
        this.auditLog = auditLog;
        AddCommand = new AsyncRelayCommand(AddAsync, onError: ex => ShowOperationError("新建处方", ex));
        EditCommand = new AsyncRelayCommand(EditAsync, onError: ex => ShowOperationError("编辑处方", ex));
        CopyCommand = new AsyncRelayCommand(CopyAsync, onError: ex =>
            ShowOperationError("复制处方", ex));
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, _ => IsAdmin, ex =>
            ShowOperationError("删除处方", ex));
        UseCommand = new RelayCommand(_ => UseSelected(), _ => SelectedPrescription is not null);
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => SelectedPrescription is not null,
            ex => ShowOperationError("导出处方", ex));
        accountService.CurrentUserChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsAdmin));
            (DeleteCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        };
        featureVisibilityService.VisibilityChanged += (_, _) => visibilityInvalidated = true;
    }

    public ObservableCollection<PrescriptionDefinition> Prescriptions { get; } = [];
    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand UseCommand { get; }
    public ICommand ExportCommand { get; }
    public bool IsAdmin => authorizationService.HasPermission(AppPermission.DeletePrescription);

    public PrescriptionDefinition? SelectedPrescription
    {
        get => selectedPrescription;
        set
        {
            if (!SetProperty(ref selectedPrescription, value)) return;
            (UseCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ExportCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public event EventHandler<PrescriptionDefinition>? UseRequested;

    public async Task InitializeAsync()
    {
        if (initialized && !visibilityInvalidated) return;
        await ReloadAsync();
        initialized = true;
        visibilityInvalidated = false;
    }

    private async Task AddAsync(CancellationToken cancellationToken)
    {
        var draft = await prescriptionService.CreateDraftAsync(cancellationToken);
        await ShowEditorAsync(draft, true, cancellationToken);
    }

    private async Task EditAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (parameter is PrescriptionDefinition prescription)
        {
            SelectedPrescription = prescription;
            await ShowEditorAsync(prescription, false, cancellationToken);
        }
    }

    private async Task CopyAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (parameter is not PrescriptionDefinition prescription) return;
        var copy = await prescriptionService.CopyAsync(prescription.Id, cancellationToken);
        await ReloadAsync(copy.Id, cancellationToken);
    }

    private async Task DeleteAsync(object? parameter, CancellationToken cancellationToken)
    {
        if (!IsAdmin || parameter is not PrescriptionDefinition prescription) return;
        var confirmed = dialogService.ConfirmWarning(
            "删除处方",
            $"确定删除处方“{prescription.DisplayName}”吗？删除后无法恢复。",
            "删除",
            "取消");
        if (!confirmed) return;

        await prescriptionService.DeleteAsync(prescription.Id, cancellationToken);
        await ReloadAsync(null, cancellationToken);
    }

    private async Task ShowEditorAsync(PrescriptionDefinition prescription, bool isNew, CancellationToken cancellationToken)
    {
        var availableTypes = FeatureCatalog.StimulationTypes
            .Where(item => featureVisibilityService.IsVisible(item.Key))
            .Select(item => item.ShortName)
            .ToArray();
        var editor = new PrescriptionEditorDialog(prescription, isNew, availableTypes)
        {
            Owner = Application.Current?.MainWindow
        };
        if (editor.ShowDialog() != true || editor.Result is not { } result) return;
        await prescriptionService.SaveAsync(result, cancellationToken);
        await ReloadAsync(result.Id, cancellationToken);
    }

    private void UseSelected()
    {
        authorizationService.RequireSignedIn();
        if (SelectedPrescription is not { } prescription) return;
        var featureKey = FeatureCatalog.StimulationTypes
            .FirstOrDefault(item => item.ShortName == prescription.StimulationType)?.Key;
        if (featureKey is null || !featureVisibilityService.IsVisible(featureKey))
        {
            dialogService.ShowInformation("使用处方", $"{prescription.StimulationType} 刺激功能当前已隐藏，无法使用该处方。");
            return;
        }
        auditLog.RecordUserAction("Use prescription");
        UseRequested?.Invoke(this, prescription);
    }

    private async Task ExportAsync(CancellationToken cancellationToken)
    {
        authorizationService.RequireSignedIn();
        if (SelectedPrescription is not { } prescription) return;
        var dialog = new SaveFileDialog
        {
            Title = "导出处方",
            Filter = "CSV 文件 (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = SanitizeFileName(prescription.DisplayName) + ".csv"
        };
        if (dialog.ShowDialog(Application.Current?.MainWindow) != true) return;

        var csv = BuildCsv(prescription);
        await File.WriteAllTextAsync(dialog.FileName, csv, new UTF8Encoding(true), cancellationToken);
        await auditTrail.AppendAsync(
            new AuditEventInput(
                AuditEventCategory.DataExport,
                "EXPORT_PRESCRIPTION_CSV",
                AuditActor.From(accountService.CurrentUser),
                "Prescription",
                prescription.Id,
                AuditEventResult.Success,
                Reason: $"file={Path.GetFileName(dialog.FileName)}"),
            cancellationToken);
        dialogService.ShowInformation("导出处方", $"处方已保存到：\n{dialog.FileName}");
    }

    private async Task ReloadAsync(string? selectedId = null, CancellationToken cancellationToken = default)
    {
        selectedId ??= SelectedPrescription?.Id;
        Prescriptions.Clear();
        nextOffset = 0;
        hasMore = true;
        await LoadMoreAsync(cancellationToken);
        SelectedPrescription = Prescriptions.FirstOrDefault(item => item.Id == selectedId) ?? Prescriptions.FirstOrDefault();
    }

    public async Task LoadMoreAsync(CancellationToken cancellationToken = default)
    {
        if (isLoading || !hasMore)
        {
            return;
        }

        isLoading = true;
        try
        {
            var targetVisibleCount = Prescriptions.Count + PageSize;
            var visibleStimulationTypes = GetVisibleStimulationTypes();
            while (hasMore && Prescriptions.Count < targetVisibleCount)
            {
                var page = await prescriptionService.GetPrescriptionsPageAsync(
                    new PageRequest(nextOffset, PageSize),
                    cancellationToken);
                foreach (var item in page.Items.Where(
                    item => IsPrescriptionVisible(item, visibleStimulationTypes)))
                {
                    Prescriptions.Add(item);
                }

                nextOffset += page.Items.Count;
                hasMore = page.HasMore;
                if (page.Items.Count == 0)
                {
                    hasMore = false;
                }
            }

            SelectedPrescription ??= Prescriptions.FirstOrDefault();
        }
        finally
        {
            isLoading = false;
        }
    }

    internal static bool IsPrescriptionVisible(
        PrescriptionDefinition prescription,
        IReadOnlySet<string> visibleStimulationTypes) =>
        visibleStimulationTypes.Contains(prescription.StimulationType);

    private IReadOnlySet<string> GetVisibleStimulationTypes() =>
        FeatureCatalog.StimulationTypes
            .Where(item => featureVisibilityService.IsVisible(item.Key))
            .Select(item => item.ShortName)
            .ToHashSet(StringComparer.Ordinal);

    internal static string BuildCsv(PrescriptionDefinition prescription)
    {
        var rows = prescription.IsPulseCurrent
            ? BuildPulseCurrentCsvRows(prescription)
            : BuildMinuteBasedCsvRows(prescription);
        var builder = new StringBuilder("参数,内容\r\n");
        foreach (var (label, value) in rows) builder.Append(Escape(label)).Append(',').Append(Escape(value)).Append("\r\n");
        return builder.ToString();
    }

    private static (string Label, string Value)[] BuildMinuteBasedCsvRows(PrescriptionDefinition prescription) =>
    [
        ("处方名称", prescription.Name), ("适应症", prescription.Indication), ("刺激模式", prescription.StimulationType),
        ("幅值", prescription.CurrentDisplay), ("模式", prescription.DeliveryMode),
        ("总时长", prescription.TotalDurationDisplay), ("间隔时间", prescription.IntervalDisplay),
        ("单次时长", prescription.SessionDurationDisplay), ("疗程", prescription.Course),
        ("渐升时间", prescription.RampUpDisplay), ("渐降时间", prescription.RampDownDisplay),
        ("证据等级", prescription.EvidenceGrade)
    ];

    private static (string Label, string Value)[] BuildPulseCurrentCsvRows(PrescriptionDefinition prescription) =>
    [
        ("处方名称", prescription.Name), ("适应症", prescription.Indication), ("刺激模式", prescription.StimulationType),
        ("幅值", prescription.CurrentDisplay), ("模式", prescription.DeliveryMode),
        ("治疗时间", prescription.TotalDurationDisplay), ("脉冲宽度", prescription.SessionDurationDisplay),
        ("上升宽度", prescription.RampUpDisplay),
        ("间隔宽度", prescription.IntervalDisplay), ("渐降时间", prescription.RampDownDisplay),
        ("疗程", prescription.Course), ("证据等级", prescription.EvidenceGrade)
    ];

    private static string Escape(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private void ShowOperationError(string title, Exception exception)
    {
        dialogService.ShowError(title, $"操作失败：{exception.Message}");
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(value) ? "处方" : value;
    }
}
