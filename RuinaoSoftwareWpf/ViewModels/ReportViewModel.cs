namespace RuinaoSoftwareWpf;

using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

public sealed class ReportViewModel : ObservableObject
{
    private readonly IStimulationRecordService stimulationRecordService;
    private readonly IUserDialogService dialogService;
    private StimulationRecordRow? selectedRecord;

    public ReportViewModel(
        IStimulationRecordService stimulationRecordService,
        IUserDialogService dialogService)
    {
        this.stimulationRecordService = stimulationRecordService;
        this.dialogService = dialogService;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, onError: ex =>
            dialogService.ShowError("记录", $"刷新失败：{ex.Message}"));
        ExportCommand = new AsyncRelayCommand(
            ExportAsync,
            parameter => parameter is StimulationRecordRow || SelectedRecord is not null,
            ex => dialogService.ShowError("处方记录", $"导出失败：{ex.Message}"));
        ReuseCommand = new RelayCommand(
            parameter => Reuse(parameter as StimulationRecordRow ?? SelectedRecord),
            parameter => parameter is StimulationRecordRow || SelectedRecord is not null);
    }

    public ObservableCollection<StimulationRecordRow> Records { get; } = [];

    public ICommand RefreshCommand { get; }

    public ICommand ExportCommand { get; }

    public ICommand ReuseCommand { get; }

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
        var records = await stimulationRecordService.GetTreatmentRecordsAsync(cancellationToken);
        Records.Clear();
        var rowNumber = 1;
        foreach (var record in records)
        {
            Records.Add(new StimulationRecordRow(rowNumber++, record));
        }

        SelectedRecord = Records.FirstOrDefault();
    }

    private async Task ExportAsync(object? parameter, CancellationToken cancellationToken)
    {
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
        dialogService.ShowInformation("处方记录", $"处方记录已保存到：\n{dialog.FileName}");
    }

    private void Reuse(StimulationRecordRow? row)
    {
        if (row is null)
        {
            return;
        }

        ReuseRequested?.Invoke(this, row.ParameterRecord);
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
