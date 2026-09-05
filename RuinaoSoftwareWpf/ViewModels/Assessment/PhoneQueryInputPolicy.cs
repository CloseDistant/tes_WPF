namespace RuinaoSoftwareWpf;

/// <summary>
/// 患者查询输入规则。只保留 ASCII 数字，并在 ViewModel 层统一限制最大长度，
/// 这样键盘输入、粘贴和代码赋值都会得到相同结果。
/// </summary>
internal static class PhoneQueryInputPolicy
{
    public const int MatchingMaximumLength = 11;

    public const int PortalMaximumLength = 20;

    public static string Normalize(string? value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value) || maximumLength <= 0)
        {
            return string.Empty;
        }

        var digits = new char[Math.Min(value.Length, maximumLength)];
        var count = 0;
        foreach (var character in value)
        {
            if (character is < '0' or > '9')
            {
                continue;
            }

            digits[count++] = character;
            if (count == maximumLength)
            {
                break;
            }
        }

        return new string(digits, 0, count);
    }
}
