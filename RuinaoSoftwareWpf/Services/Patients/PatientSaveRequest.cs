namespace RuinaoSoftwareWpf;

public sealed record PatientSaveRequest(
    string? PatientCode,
    string Name,
    PatientSex? Sex,
    DateOnly? BirthDate,
    string? IdCardNumber,
    string? Phone,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    string? HomeAddress,
    string? ClinicalInfo);
