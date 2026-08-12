namespace RuinaoSoftwareWpf;

public sealed record PatientRecord(
    string PatientCode,
    string Name,
    PatientSex Sex,
    DateOnly BirthDate,
    int Age,
    string? IdCardNumber,
    string Phone,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    string? HomeAddress,
    string? ClinicalInfo,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
