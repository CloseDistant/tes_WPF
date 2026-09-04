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
    private bool isSelectingFollowUp;

    public ExternalFollowUpPatientRowViewModel(ExternalFollowUpPatient patient)
    {
        Patient = patient;
        SelectFollowUpCommand = new RelayCommand(
            parameter => SelectFollowUp(parameter),
            parameter => !IsSelectingFollowUp
                && parameter is ExternalFollowUpDetail detail
                && CanSelectFollowUp(detail));
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

    public bool IsSelectingFollowUp
    {
        get => isSelectingFollowUp;
        private set
        {
            if (SetProperty(ref isSelectingFollowUp, value))
            {
                (SelectFollowUpCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }

    public string SelectedFollowUpSummary => SelectedFollowUp is null
        ? string.Empty
        : $"已选择：{SelectedFollowUp.SettingName ?? SelectedFollowUp.Name ?? "随访"} · detailId {SelectedFollowUp.Id}";

    public event EventHandler<ExternalFollowUpDetail>? FollowUpSelected;

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
            IsSelectingFollowUp = true;
            FollowUpSelected?.Invoke(this, detail);
        }
    }

    public void CompleteFollowUpSelection()
    {
        IsSelectingFollowUp = false;
    }

    private static bool CanSelectFollowUp(ExternalFollowUpDetail detail)
    {
        if (!detail.Id.HasValue)
        {
            return false;
        }

        return string.Equals(detail.FlowStatusName, "待测评", StringComparison.OrdinalIgnoreCase);
    }
}
