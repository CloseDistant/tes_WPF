namespace RuinaoSoftwareWpf;

public static class InputTextRules
{
    public static bool ContainsControlCharacters(string? value) =>
        !string.IsNullOrEmpty(value) && value.Any(char.IsControl);

    public static bool HasContent(string? value) => !string.IsNullOrWhiteSpace(value);
}
