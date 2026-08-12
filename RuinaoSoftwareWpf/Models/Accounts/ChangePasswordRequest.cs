namespace RuinaoSoftwareWpf;

public sealed record ChangePasswordRequest(long UserId, string NewPassword, string ConfirmPassword);
