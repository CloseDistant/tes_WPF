namespace RuinaoHardwareDebugWpf;

public enum PatientFormMode
{
    Create,
    Edit
}

public sealed record PatientValidationError(string FieldName, string Message);

public sealed class PatientValidationResult
{
    public PatientValidationResult(IReadOnlyList<PatientValidationError> errors)
    {
        Errors = errors;
    }

    public IReadOnlyList<PatientValidationError> Errors { get; }

    public bool IsValid => Errors.Count == 0;

    public string Message => Errors.FirstOrDefault()?.Message ?? string.Empty;

    public bool HasError(string fieldName) => Errors.Any(item => item.FieldName == fieldName);
}

public static class PatientSaveRequestValidator
{
    public const string PatientCodeField = nameof(PatientSaveRequest.PatientCode);
    public const string NameField = nameof(PatientSaveRequest.Name);
    public const string SexField = nameof(PatientSaveRequest.Sex);
    public const string BirthDateField = nameof(PatientSaveRequest.BirthDate);
    public const string PhoneField = nameof(PatientSaveRequest.Phone);

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

        if (mode == PatientFormMode.Edit && string.IsNullOrWhiteSpace(request.Phone))
        {
            errors.Add(new(PhoneField, "联系电话不能为空"));
        }

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
}
