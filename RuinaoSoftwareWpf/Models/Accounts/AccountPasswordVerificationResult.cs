namespace RuinaoSoftwareWpf;

public sealed record AccountPasswordVerificationResult(bool Succeeded, bool IsBlocked, string Message);
