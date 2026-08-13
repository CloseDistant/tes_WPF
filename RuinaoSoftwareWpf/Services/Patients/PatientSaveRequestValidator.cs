namespace RuinaoSoftwareWpf;

using System.Text.RegularExpressions;

public static class PatientSaveRequestValidator
{
    public const int PatientNameMaxLength = 20;
    public const int ClinicalInfoMaxLength = 500;
    public const int IdCardNumberMaxLength = 30;
    public const int PhoneMaxLength = 20;
    public const int EmergencyContactNameMaxLength = 20;
    public const int HomeAddressMaxLength = 200;
    public const string PatientCodeField = nameof(PatientSaveRequest.PatientCode);
    public const string NameField = nameof(PatientSaveRequest.Name);
    public const string SexField = nameof(PatientSaveRequest.Sex);
    public const string BirthDateField = nameof(PatientSaveRequest.BirthDate);
    public const string IdCardNumberField = nameof(PatientSaveRequest.IdCardNumber);
    public const string PhoneField = nameof(PatientSaveRequest.Phone);
    public const string EmergencyContactNameField = nameof(PatientSaveRequest.EmergencyContactName);
    public const string EmergencyContactPhoneField = nameof(PatientSaveRequest.EmergencyContactPhone);
    public const string HomeAddressField = nameof(PatientSaveRequest.HomeAddress);
    public const string ClinicalInfoField = nameof(PatientSaveRequest.ClinicalInfo);

    private static readonly Regex PatientNamePattern = new(
        @"^(?:\p{L}\p{M}*)+(?:[ ·-](?:\p{L}\p{M}*)+)*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static PatientValidationResult Validate(PatientSaveRequest request, PatientFormMode mode)
    {
        var errors = new List<PatientValidationError>();
        if (mode == PatientFormMode.Edit && string.IsNullOrWhiteSpace(request.PatientCode))
        {
            errors.Add(new(PatientCodeField, "患者 ID 不能为空"));
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors.Add(new(NameField, "姓名不能为空"));
        }
        else if (request.Name.Trim().Length > PatientNameMaxLength)
        {
            errors.Add(new(NameField, $"姓名不能超过 {PatientNameMaxLength} 个字符"));
        }
        else if (!IsSupportedPersonName(request.Name.Trim()))
        {
            errors.Add(new(NameField, "姓名格式不正确，仅支持文字、空格、间隔号（·）和连字符（-）"));
        }

        if (request.Sex is null)
        {
            errors.Add(new(SexField, "性别不能为空"));
        }

        if (request.BirthDate is null)
        {
            errors.Add(new(BirthDateField, "出生日期不能为空"));
        }
        else if (request.BirthDate > DateOnly.FromDateTime(DateTime.Today))
        {
            errors.Add(new(BirthDateField, "出生日期不能晚于今天"));
        }

        if (mode == PatientFormMode.Create)
        {
            ValidateOptionalText(
                errors,
                ClinicalInfoField,
                "临床信息",
                request.ClinicalInfo,
                ClinicalInfoMaxLength,
                allowLineBreaks: true);
        }

        if (mode != PatientFormMode.Edit)
        {
            return new PatientValidationResult(errors);
        }

        if (!string.IsNullOrEmpty(request.IdCardNumber)
            && request.IdCardNumber.Length > IdCardNumberMaxLength)
        {
            errors.Add(new(IdCardNumberField, $"身份证号不能超过 {IdCardNumberMaxLength} 个字符"));
        }

        if (string.IsNullOrWhiteSpace(request.Phone))
        {
            errors.Add(new(PhoneField, "联系电话不能为空"));
        }
        else if (!string.IsNullOrWhiteSpace(request.Phone) && !ContainsOnlyAsciiDigits(request.Phone))
        {
            errors.Add(new(PhoneField, "联系电话只能填写数字"));
        }
        else if (request.Phone.Length > PhoneMaxLength)
        {
            errors.Add(new(PhoneField, $"联系电话不能超过 {PhoneMaxLength} 位"));
        }

        if (!string.IsNullOrWhiteSpace(request.EmergencyContactName))
        {
            if (request.EmergencyContactName.Length > EmergencyContactNameMaxLength)
            {
                errors.Add(new(
                    EmergencyContactNameField,
                    $"紧急联系人姓名不能超过 {EmergencyContactNameMaxLength} 个字符"));
            }
            else if (!IsSupportedPersonName(request.EmergencyContactName))
            {
                errors.Add(new(
                    EmergencyContactNameField,
                    "紧急联系人姓名格式不正确，仅支持文字、空格、间隔号（·）和连字符（-）"));
            }
        }

        if (!string.IsNullOrWhiteSpace(request.EmergencyContactPhone)
            && !ContainsOnlyAsciiDigits(request.EmergencyContactPhone))
        {
            errors.Add(new(EmergencyContactPhoneField, "紧急联系人电话只能填写数字"));
        }
        else if (!string.IsNullOrWhiteSpace(request.EmergencyContactPhone)
                 && request.EmergencyContactPhone.Length > PhoneMaxLength)
        {
            errors.Add(new(EmergencyContactPhoneField, $"紧急联系人电话不能超过 {PhoneMaxLength} 位"));
        }

        ValidateOptionalText(
            errors,
            HomeAddressField,
            "家庭住址",
            request.HomeAddress,
            HomeAddressMaxLength,
            allowLineBreaks: false);

        return new PatientValidationResult(errors);
    }

    public static void EnsureValid(PatientSaveRequest request, PatientFormMode mode)
    {
        var result = Validate(request, mode);
        if (!result.IsValid)
        {
            throw new InvalidOperationException(result.Message);
        }
    }

    public static bool ContainsOnlyAsciiDigits(string value)
    {
        return value.All(character => character is >= '0' and <= '9');
    }

    public static bool IsSupportedPersonName(string value)
    {
        return PatientNamePattern.IsMatch(value);
    }

    public static bool ContainsDisallowedControlCharacters(string value, bool allowLineBreaks)
    {
        return value.Any(character =>
            char.IsControl(character)
            && !(allowLineBreaks && character is '\r' or '\n'));
    }

    private static void ValidateOptionalText(
        ICollection<PatientValidationError> errors,
        string fieldName,
        string displayName,
        string? value,
        int maxLength,
        bool allowLineBreaks)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        if (value.Length > maxLength)
        {
            errors.Add(new(fieldName, $"{displayName}不能超过 {maxLength} 个字符"));
        }
        else if (ContainsDisallowedControlCharacters(value, allowLineBreaks))
        {
            errors.Add(new(fieldName, $"{displayName}包含不支持的控制字符"));
        }
    }
}
