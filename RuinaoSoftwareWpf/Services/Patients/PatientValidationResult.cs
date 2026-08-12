namespace RuinaoSoftwareWpf;

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
