namespace RuinaoSoftwareWpf;

/// <summary>
/// 患者信息 ViewModel。
/// 目前只有硬编码演示数据，后续接入 IPatientService 做持久化。
/// </summary>
public sealed class PatientViewModel : ObservableObject
{
    private readonly IPatientService patientService;
    private string patientCode = string.Empty;
    private string name = string.Empty;
    private string sex = string.Empty;
    private string age = string.Empty;
    private string symptom = string.Empty;

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
            PatientCode = string.Empty;
            Name = string.Empty;
            Sex = string.Empty;
            Age = string.Empty;
            Symptom = string.Empty;
            return;
        }

        PatientCode = patient.PatientCode;
        Name = patient.Name;
        Sex = patient.Sex.ToDisplayText();
        Age = patient.Age.ToString();
        Symptom = string.IsNullOrWhiteSpace(patient.ClinicalInfo) ? string.Empty : patient.ClinicalInfo;
    }
}
