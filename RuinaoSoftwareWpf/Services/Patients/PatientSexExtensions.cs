namespace RuinaoSoftwareWpf;

public static class PatientSexExtensions
{
    public static string ToDisplayText(this PatientSex sex) => sex switch
    {
        PatientSex.Female => "女",
        PatientSex.Male => "男",
        _ => "-"
    };

    public static string? ToStorageCode(this PatientSex sex) => sex switch
    {
        PatientSex.Female => "F",
        PatientSex.Male => "M",
        _ => null
    };

    public static PatientSex FromStorageCode(string? value) => value?.Trim() switch
    {
        "F" or "female" or "Female" or "女" or "女性" => PatientSex.Female,
        "M" or "male" or "Male" or "男" or "男性" => PatientSex.Male,
        _ => PatientSex.Unknown
    };
}
