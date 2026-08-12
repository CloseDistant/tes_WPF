namespace RuinaoSoftwareWpf;

public sealed record ResetPasswordRequest(long UserId, string NewPassword, string ConfirmPassword);
