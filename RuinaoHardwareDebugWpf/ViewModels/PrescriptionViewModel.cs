namespace RuinaoHardwareDebugWpf;

using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using RuinaoHardwareDebugWpf.Views.Dialogs;

public sealed class PrescriptionViewModel : ObservableObject
{
    private readonly IPrescriptionService prescriptionService;
    private readonly IUserDialogService dialogService;
    private readonly IFeatureVisibilityService featureVisibilityService;
    private readonly IAccountService accountService;
    private PrescriptionDefinition? selectedPrescription;
    private bool initialized;

    public PrescriptionViewModel(
        IPrescriptionService prescriptionService,
        IUserDialogService dialogService,
        IFeatureVisibilityService featureVisibilityService,
        IAccountService accountService)
    {
        this.prescriptionService = prescriptionService;
        this.dialogService = dialogService;
        this.featureVisibilityService = featureVisibilityService;
        this.accountService = accountService;
        AddCommand = new AsyncRelayCommand(AddAsync);
        EditCommand = new AsyncRelayCommand(EditAsync);
        CopyCommand = new AsyncRelayCommand(CopyAsync, onError: ex =>
            dialogService.ShowError("复制处方", $"复制失败：{ex.Message}"));
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, _ => IsAdmin, ex =>
            dialogService.ShowError("删除处方", $"删除失败：{ex.Message}"));
        UseCommand = new RelayCommand(_ => UseSelected(), _ => SelectedPrescription is not null);
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => SelectedPrescription is not null,
            ex => dialogService.ShowError("导出处方", $"导出失败：{ex.Message}"));
        accountService.CurrentUserChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsAdmin));
            (DeleteCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        };
    }

    public ObservableCollection<PrescriptionDefinition> Prescriptions { get; } = [];
    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand UseCommand { get; }
    public ICommand ExportCommand { get; }
    public bool IsAdmin => accountService.IsCurrentUserAdmin();

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
        if (initialized) return;
        initialized = true;
        await ReloadAsync();
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
        if (SelectedPrescription is not { } prescription) return;
        var featureKey = FeatureCatalog.StimulationTypes
            .FirstOrDefault(item => item.ShortName == prescription.StimulationType)?.Key;
        if (featureKey is null || !featureVisibilityService.IsVisible(featureKey))
        {
            dialogService.ShowInformation("使用处方", $"{prescription.StimulationType} 刺激功能当前已隐藏，无法使用该处方。");
            return;
        }
        UseRequested?.Invoke(this, prescription);
    }

    private async Task ExportAsync(CancellationToken cancellationToken)
    {
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
        dialogService.ShowInformation("导出处方", $"处方已保存到：\n{dialog.FileName}");
    }

    private async Task ReloadAsync(string? selectedId = null, CancellationToken cancellationToken = default)
    {
        selectedId ??= SelectedPrescription?.Id;
        var items = await prescriptionService.GetPrescriptionsAsync(cancellationToken);
        Prescriptions.Clear();
        foreach (var item in items) Prescriptions.Add(item);
        SelectedPrescription = Prescriptions.FirstOrDefault(item => item.Id == selectedId) ?? Prescriptions.FirstOrDefault();
    }

    internal static string BuildCsv(PrescriptionDefinition prescription)
    {
        var rows = new (string Label, string Value)[]
        {
            ("处方名称", prescription.Name), ("适应症", prescription.Indication), ("刺激模式", prescription.StimulationType),
            ("电流强度", prescription.CurrentDisplay), ("模式", prescription.DeliveryMode),
            ("总时长", prescription.TotalDurationDisplay), ("间隔时间", prescription.IntervalDisplay),
            ("单次时长", prescription.SessionDurationDisplay), ("疗程", prescription.Course),
            ("渐升/渐降", prescription.RampDisplay), ("证据等级", prescription.EvidenceGrade)
        };
        var builder = new StringBuilder("参数,内容\r\n");
        foreach (var (label, value) in rows) builder.Append(Escape(label)).Append(',').Append(Escape(value)).Append("\r\n");
        return builder.ToString();
    }

    private static string Escape(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(value) ? "处方" : value;
    }
}
