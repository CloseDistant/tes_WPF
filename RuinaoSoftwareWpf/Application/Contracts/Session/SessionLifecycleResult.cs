namespace RuinaoSoftwareWpf;

public sealed record SessionLifecycleResult(
    bool Succeeded, string Message, SessionLifecycleConfirmationRequest? Confirmation = null);
