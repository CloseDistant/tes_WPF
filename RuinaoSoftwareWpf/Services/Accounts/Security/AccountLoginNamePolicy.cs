namespace RuinaoSoftwareWpf;

/// <summary>
/// 登录名在账号创建和身份验证入口共用的格式规则。
/// </summary>
public static class AccountLoginNamePolicy
{
    public const int MinimumLength = 1;
    public const int MaximumLength = 32;

    public const string RequirementText =
        "登录名须为 1～32 个字符，且只能使用英文字母和数字";

    public static bool IsValid(string? loginName)
    {
        return loginName is not null
            && loginName.Length is >= MinimumLength and <= MaximumLength
            && loginName.All(static character => character is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9');
    }

    public static void Validate(string? loginName)
    {
        if (!IsValid(loginName))
        {
            throw new InvalidOperationException(RequirementText);
        }
    }
}
