namespace RuinaoSoftwareWpf;

public sealed record SessionUnlockResult(
    bool Succeeded,
    bool IsBlocked,
    string Message);
