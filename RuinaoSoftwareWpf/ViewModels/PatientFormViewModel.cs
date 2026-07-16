namespace RuinaoSoftwareWpf;

using System.Globalization;

public sealed class PatientFormViewModel : ObservableObject
{
    private string name = string.Empty;
    private PatientSex? sex;
    private string birthDateText = string.Empty;
    private string idCardNumber = string.Empty;
    private string phone = string.Empty;
    private string emergencyContactName = string.Empty;
    private string emergencyContactPhone = string.Empty;
    private string homeAddress = string.Empty;
    private string clinicalInfo = string.Empty;
    private string errorMessage = string.Empty;

    public PatientFormViewModel(PatientRecord? patient)
    {
        Mode = patient is null ? PatientFormMode.Create : PatientFormMode.Edit;
        PatientCode = patient?.PatientCode;
        Name = patient?.Name ?? string.Empty;
        Sex = patient?.Sex ?? PatientSex.Male;
        BirthDateText = patient?.BirthDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
        IdCardNumber = patient?.IdCardNumber ?? string.Empty;
        Phone = patient?.Phone ?? string.Empty;
        EmergencyContactName = patient?.EmergencyContactName ?? string.Empty;
        EmergencyContactPhone = patient?.EmergencyContactPhone ?? string.Empty;
        HomeAddress = patient?.HomeAddress ?? string.Empty;
        ClinicalInfo = patient?.ClinicalInfo ?? string.Empty;
    }

    public PatientFormMode Mode { get; }
    public bool IsCreateMode => Mode == PatientFormMode.Create;
    public bool IsEditMode => Mode == PatientFormMode.Edit;
    public string Title => IsCreateMode ? "新增患者" : "编辑患者信息";
    public string? PatientCode { get; }

    public string Name { get => name; set => SetProperty(ref name, value); }
    public PatientSex? Sex { get => sex; set => SetProperty(ref sex, value); }
    public bool IsMale { get => Sex == PatientSex.Male; set { if (value) Sex = PatientSex.Male; } }
    public bool IsFemale { get => Sex == PatientSex.Female; set { if (value) Sex = PatientSex.Female; } }
    public string BirthDateText { get => birthDateText; set => SetProperty(ref birthDateText, value); }
    public string IdCardNumber { get => idCardNumber; set => SetProperty(ref idCardNumber, value); }
    public string Phone { get => phone; set => SetProperty(ref phone, value); }
    public string EmergencyContactName { get => emergencyContactName; set => SetProperty(ref emergencyContactName, value); }
    public string EmergencyContactPhone { get => emergencyContactPhone; set => SetProperty(ref emergencyContactPhone, value); }
    public string HomeAddress { get => homeAddress; set => SetProperty(ref homeAddress, value); }
    public string ClinicalInfo { get => clinicalInfo; set => SetProperty(ref clinicalInfo, value); }
    public string ErrorMessage { get => errorMessage; private set => SetProperty(ref errorMessage, value); }

    public PatientValidationResult Validate(out PatientSaveRequest request)
    {
        var hasBirthDate = DateOnly.TryParseExact(
            BirthDateText.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var birthDate);
        request = new PatientSaveRequest(
            PatientCode,
            Name.Trim(),
            Sex,
            hasBirthDate ? birthDate : null,
            Normalize(IdCardNumber),
            Normalize(Phone),
            Normalize(EmergencyContactName),
            Normalize(EmergencyContactPhone),
            Normalize(HomeAddress),
            Normalize(ClinicalInfo));
        var result = PatientSaveRequestValidator.Validate(request, Mode);
        ErrorMessage = result.Message;
        return result;
    }

    public void ClearError() => ErrorMessage = string.Empty;

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
