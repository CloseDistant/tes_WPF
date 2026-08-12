namespace RuinaoSoftwareWpf;

public sealed record AccountLoginResult(bool Succeeded, CurrentUserInfo? User, string Message);
