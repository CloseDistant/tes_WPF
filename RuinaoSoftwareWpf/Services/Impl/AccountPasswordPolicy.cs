namespace RuinaoSoftwareWpf;

using System.Globalization;
using System.Text;

/// <summary>
/// 账号密码的统一复杂度策略。登录验证不调用本策略，以兼容既有密码和初始 Admin 密码。
/// </summary>
public static class AccountPasswordPolicy
{
    public const int MinimumLength = 8;
    public const int MaximumLength = 20;

    public const string RequirementText =
        "密码须为 8～20 个字符，且至少包含字母、数字、特殊字符中的两类，不允许包含空格";

    public static void Validate(string password, string confirmPassword)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException("密码不能为空");
        }

        if (password != confirmPassword)
        {
            throw new InvalidOperationException("两次输入的密码不一致");
        }

        if (password.EnumerateRunes().Any(Rune.IsWhiteSpace))
        {
            throw new InvalidOperationException("密码不能包含空格或其他空白字符");
        }

        var characterCount = new StringInfo(password).LengthInTextElements;
        if (characterCount is < MinimumLength or > MaximumLength)
        {
            throw new InvalidOperationException("密码长度必须为 8～20 个字符");
        }

        var containsLetter = false;
        var containsDigit = false;
        var containsSpecialCharacter = false;

        foreach (var rune in password.EnumerateRunes())
        {
            if (Rune.IsLetter(rune))
            {
                containsLetter = true;
            }
            else if (Rune.IsDigit(rune))
            {
                containsDigit = true;
            }
            else
            {
                containsSpecialCharacter = true;
            }
        }

        var categoryCount = Convert.ToInt32(containsLetter)
            + Convert.ToInt32(containsDigit)
            + Convert.ToInt32(containsSpecialCharacter);
        if (categoryCount < 2)
        {
            throw new InvalidOperationException("密码至少需要包含字母、数字、特殊字符三类中的两类");
        }
    }
}
