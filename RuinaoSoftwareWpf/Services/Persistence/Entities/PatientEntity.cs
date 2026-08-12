namespace RuinaoSoftwareWpf;

internal sealed class PatientEntity
{
    public long Id { get; set; }
    public long? OwnerUserId { get; set; }
    public long? UpdatedByUserId { get; set; }
    public string PatientCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Gender { get; set; }
    public long? BirthDateUnixMs { get; set; }
    public string? IdCardEncrypted { get; set; }
    public string? PhoneEncrypted { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhoneEncrypted { get; set; }
    public string? HomeAddress { get; set; }
    public string? ClinicalInfo { get; set; }
    public long CreatedAtUnixMs { get; set; }
    public long UpdatedAtUnixMs { get; set; }
}
