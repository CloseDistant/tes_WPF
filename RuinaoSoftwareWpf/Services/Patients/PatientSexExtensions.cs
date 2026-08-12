namespace RuinaoSoftwareWpf;

public static class PatientSexExtensions
{
    public static string ToDisplayText(this PatientSex sex) => sex == PatientSex.Female ? "女" : "男";

    public static string ToStorageCode(this PatientSex sex) => sex == PatientSex.Female ? "F" : "M";

    public static PatientSex FromStorageCode(string? value) => value?.Trim() switch
    {
        "F" or "female" or "Female" or "女" or "女性" => PatientSex.Female,
        _ => PatientSex.Male
    };
}
