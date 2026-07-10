namespace RuinaoHardwareDebugWpf;

/// <summary>
/// 患者信息 ViewModel。
/// 目前只有硬编码演示数据，后续接入 IPatientService 做持久化。
/// </summary>
public sealed class PatientViewModel : ObservableObject
{
    private readonly IPatientService patientService;
    private string patientCode = "未选择患者";
    private string name = "-";
    private string sex = "-";
    private string age = "-";
    private string symptom = "请新增或选择患者";

    public PatientViewModel(IPatientService patientService)
    {
        this.patientService = patientService;
        patientService.CurrentPatientChanged += (_, _) => ApplyCurrentPatient();
    }

    public string PatientCode { get => patientCode; set => SetProperty(ref patientCode, value); }
    public string Name { get => name; set => SetProperty(ref name, value); }
    public string Sex { get => sex; set => SetProperty(ref sex, value); }
    public string Age { get => age; set => SetProperty(ref age, value); }
    public string Symptom { get => symptom; set => SetProperty(ref symptom, value); }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await patientService.InitializeAsync(cancellationToken);
        ApplyCurrentPatient();
    }

    private void ApplyCurrentPatient()
    {
        var patient = patientService.CurrentPatient;
        if (patient is null)
        {
            PatientCode = "未选择患者";
            Name = "-";
            Sex = "-";
            Age = "-";
            Symptom = "请新增或选择患者";
            return;
        }

        PatientCode = patient.PatientCode;
        Name = patient.Name;
        Sex = patient.Sex.ToDisplayText();
        Age = patient.Age.ToString();
        Symptom = string.IsNullOrWhiteSpace(patient.ClinicalInfo) ? string.Empty : patient.ClinicalInfo;
    }
}
