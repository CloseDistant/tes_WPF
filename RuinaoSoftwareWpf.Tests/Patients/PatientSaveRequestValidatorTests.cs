namespace RuinaoSoftwareWpf.Tests;

using Xunit;

public sealed class PatientSaveRequestValidatorTests
{
    public static TheoryData<string> ValidPatientNames => new()
    {
        "张伟",
        "阿不都·热合曼",
        "Mary Smith",
        "Jean-Pierre"
    };

    public static TheoryData<string> InvalidPatientNames => new()
    {
        "张伟123",
        "张@伟",
        "😀张伟",
        "<script>alert(1)</script>",
        "·张伟",
        "张伟-",
        "张--伟",
        "张 ·伟"
    };

    [Theory]
    [MemberData(nameof(ValidPatientNames))]
    public void Validate_Create_AllowsSupportedPatientNames(string name)
    {
        var result = PatientSaveRequestValidator.Validate(CreateRequest(name: name), PatientFormMode.Create);

        Assert.False(result.HasError(PatientSaveRequestValidator.NameField));
    }

    [Theory]
    [MemberData(nameof(InvalidPatientNames))]
    public void Validate_Create_RejectsUnsupportedPatientNames(string name)
    {
        var result = PatientSaveRequestValidator.Validate(CreateRequest(name: name), PatientFormMode.Create);

        Assert.True(result.HasError(PatientSaveRequestValidator.NameField));
    }

    [Fact]
    public void Validate_Create_RejectsClinicalInfoOverMaximumLength()
    {
        var result = PatientSaveRequestValidator.Validate(
            CreateRequest(clinicalInfo: new string('病', PatientSaveRequestValidator.ClinicalInfoMaxLength + 1)),
            PatientFormMode.Create);

        Assert.True(result.HasError(PatientSaveRequestValidator.ClinicalInfoField));
    }

    [Fact]
    public void Validate_Create_RejectsDisallowedControlCharacterInClinicalInfo()
    {
        var result = PatientSaveRequestValidator.Validate(
            CreateRequest(clinicalInfo: "情况稳定\u0001"),
            PatientFormMode.Create);

        Assert.True(result.HasError(PatientSaveRequestValidator.ClinicalInfoField));
    }

    [Fact]
    public void Validate_Create_AllowsLineBreaksInClinicalInfo()
    {
        var result = PatientSaveRequestValidator.Validate(
            CreateRequest(clinicalInfo: "第一行\r\n第二行"),
            PatientFormMode.Create);

        Assert.False(result.HasError(PatientSaveRequestValidator.ClinicalInfoField));
    }

    [Fact]
    public void Validate_Create_AllowsNameAtMaximumLength()
    {
        var result = PatientSaveRequestValidator.Validate(
            CreateRequest(name: new string('张', PatientSaveRequestValidator.PatientNameMaxLength)),
            PatientFormMode.Create);

        Assert.False(result.HasError(PatientSaveRequestValidator.NameField));
    }

    [Fact]
    public void Validate_Edit_DoesNotRejectHiddenExistingClinicalInfo()
    {
        var result = PatientSaveRequestValidator.Validate(
            CreateRequest(
                patientCode: "P202608130001",
                phone: "13800138000",
                clinicalInfo: new string('病', PatientSaveRequestValidator.ClinicalInfoMaxLength + 1)),
            PatientFormMode.Edit);

        Assert.False(result.HasError(PatientSaveRequestValidator.ClinicalInfoField));
    }

    [Fact]
    public void Validate_Edit_RejectsFieldsOverTheirMaximumLengths()
    {
        var request = CreateRequest(
            patientCode: "P202608130001",
            idCardNumber: new string('1', PatientSaveRequestValidator.IdCardNumberMaxLength + 1),
            phone: new string('1', PatientSaveRequestValidator.PhoneMaxLength + 1),
            emergencyContactName: new string('王', PatientSaveRequestValidator.EmergencyContactNameMaxLength + 1),
            emergencyContactPhone: new string('2', PatientSaveRequestValidator.PhoneMaxLength + 1),
            homeAddress: new string('路', PatientSaveRequestValidator.HomeAddressMaxLength + 1));

        var result = PatientSaveRequestValidator.Validate(request, PatientFormMode.Edit);

        Assert.True(result.HasError(PatientSaveRequestValidator.IdCardNumberField));
        Assert.True(result.HasError(PatientSaveRequestValidator.PhoneField));
        Assert.True(result.HasError(PatientSaveRequestValidator.EmergencyContactNameField));
        Assert.True(result.HasError(PatientSaveRequestValidator.EmergencyContactPhoneField));
        Assert.True(result.HasError(PatientSaveRequestValidator.HomeAddressField));
    }

    [Fact]
    public void Validate_Edit_RejectsInvalidEmergencyContactNameAndAddressControlCharacter()
    {
        var result = PatientSaveRequestValidator.Validate(
            CreateRequest(
                patientCode: "P202608130001",
                phone: "13800138000",
                emergencyContactName: "王@强",
                homeAddress: "某市\u0001某路"),
            PatientFormMode.Edit);

        Assert.True(result.HasError(PatientSaveRequestValidator.EmergencyContactNameField));
        Assert.True(result.HasError(PatientSaveRequestValidator.HomeAddressField));
    }

    [Fact]
    public void Validate_Edit_RejectsNonDigitPhoneNumbers()
    {
        var result = PatientSaveRequestValidator.Validate(
            CreateRequest(
                patientCode: "P202608130001",
                phone: "138-0013-8000",
                emergencyContactPhone: "010A"),
            PatientFormMode.Edit);

        Assert.True(result.HasError(PatientSaveRequestValidator.PhoneField));
        Assert.True(result.HasError(PatientSaveRequestValidator.EmergencyContactPhoneField));
    }

    private static PatientSaveRequest CreateRequest(
        string? patientCode = null,
        string name = "张伟",
        string? idCardNumber = null,
        string? phone = null,
        string? emergencyContactName = null,
        string? emergencyContactPhone = null,
        string? homeAddress = null,
        string? clinicalInfo = null) =>
        new(
            patientCode,
            name,
            PatientSex.Male,
            new DateOnly(1990, 1, 1),
            idCardNumber,
            phone,
            emergencyContactName,
            emergencyContactPhone,
            homeAddress,
            clinicalInfo);
}
