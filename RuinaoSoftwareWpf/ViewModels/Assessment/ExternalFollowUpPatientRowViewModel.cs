namespace RuinaoSoftwareWpf;

using System.Collections.ObjectModel;
using System.Windows.Input;

/// <summary>
/// 匹配列表中的一行。把接口患者摘要和该行的展开状态隔离，确保同一页最多展开一名患者。
/// </summary>
public sealed class ExternalFollowUpPatientRowViewModel : ObservableObject
{
    private bool isExpanded;
    private bool isLoadingFollowUps;
    private string followUpError = string.Empty;
    private ExternalFollowUpDetail? selectedFollowUp;

    public ExternalFollowUpPatientRowViewModel(ExternalFollowUpPatient patient)
    {
        Patient = patient;
        SelectFollowUpCommand = new RelayCommand(
            parameter => SelectFollowUp(parameter),
            parameter => parameter is ExternalFollowUpDetail detail && CanSelectFollowUp(detail));
    }

    public ExternalFollowUpPatient Patient { get; }

    public string Type => Patient.Type;
    public string Name => Patient.Name;
    public string DepartmentName => Patient.DepartmentName;
    public string BatchNumber => Patient.BatchNumber;
    public string PatientId => Patient.PatientId;
    public string Phone => Patient.Phone;

    public ObservableCollection<ExternalFollowUpDetail> FollowUps { get; } = [];

    public ICommand SelectFollowUpCommand { get; }

    public ExternalFollowUpDetail? SelectedFollowUp
    {
        get => selectedFollowUp;
        private set
        {
            if (SetProperty(ref selectedFollowUp, value))
            {
                OnPropertyChanged(nameof(HasSelectedFollowUp));
                OnPropertyChanged(nameof(SelectedFollowUpSummary));
            }
        }
    }

    public bool HasSelectedFollowUp => SelectedFollowUp is not null;

    public string SelectedFollowUpSummary => SelectedFollowUp is null
        ? string.Empty
        : $"已选择：{SelectedFollowUp.SettingName ?? SelectedFollowUp.Name ?? "随访"} · detailId {SelectedFollowUp.Id}";

    public bool IsExpanded
    {
        get => isExpanded;
        set => SetProperty(ref isExpanded, value);
    }

    public bool IsLoadingFollowUps
    {
        get => isLoadingFollowUps;
        set => SetProperty(ref isLoadingFollowUps, value);
    }

    public string FollowUpError
    {
        get => followUpError;
        set
        {
            if (SetProperty(ref followUpError, value))
            {
                OnPropertyChanged(nameof(HasFollowUpError));
            }
        }
    }

    public bool HasFollowUpError => !string.IsNullOrWhiteSpace(FollowUpError);

    public bool HasFollowUps => FollowUps.Count > 0;

    public void ReplaceFollowUps(IEnumerable<ExternalFollowUpDetail> details)
    {
        SelectedFollowUp = null;
        FollowUps.Clear();
        foreach (var detail in details)
        {
            FollowUps.Add(detail);
        }

        OnPropertyChanged(nameof(HasFollowUps));
    }

    public void ClearFollowUps()
    {
        SelectedFollowUp = null;
        if (FollowUps.Count == 0)
        {
            return;
        }

        FollowUps.Clear();
        OnPropertyChanged(nameof(HasFollowUps));
    }

    private void SelectFollowUp(object? parameter)
    {
        if (parameter is ExternalFollowUpDetail detail && CanSelectFollowUp(detail))
        {
            SelectedFollowUp = detail;
        }
    }

    private static bool CanSelectFollowUp(ExternalFollowUpDetail detail)
    {
        if (!detail.Id.HasValue)
        {
            return false;
        }

        if (string.Equals(detail.FlowStatusName, "已完成", StringComparison.OrdinalIgnoreCase)
            || string.Equals(detail.FlowStatusName, "已过期", StringComparison.OrdinalIgnoreCase)
            || string.Equals(detail.FlowStatusName, "已停止", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(detail.FlowStatusName)
            || (!string.Equals(detail.QuestionnaireStatusName, "已完成", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(detail.QuestionnaireStatusName, "已过期", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(detail.QuestionnaireStatusName, "已停止", StringComparison.OrdinalIgnoreCase));
    }
}
