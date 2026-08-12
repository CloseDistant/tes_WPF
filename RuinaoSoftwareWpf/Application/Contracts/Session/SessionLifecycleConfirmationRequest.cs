namespace RuinaoSoftwareWpf;

public sealed record SessionLifecycleConfirmationRequest(
    string SessionKey, string Title, string Message, string ConfirmText,
    string CancelText, string CancelledResultMessage);
