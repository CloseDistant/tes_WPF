namespace RuinaoSoftwareWpf;

public sealed record CreateAccountRequest(
    string LoginName,
    string Password,
    string ConfirmPassword,
    string DisplayName,
    int RoleId);
