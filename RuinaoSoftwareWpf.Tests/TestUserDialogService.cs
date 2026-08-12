namespace RuinaoSoftwareWpf.Tests;

internal sealed class TestUserDialogService : IUserDialogService
{
    public bool ConfirmationResult { get; set; } = true;

    public bool ChannelStartConfirmationResult { get; set; } = true;

    public bool SynchronizedStartConfirmationResult { get; set; } = true;

    public string? LastConfirmationTitle { get; private set; }

    public string? LastConfirmationMessage { get; private set; }

    public DirectCurrentChannelStartConfirmationRequest? LastChannelStartRequest { get; private set; }

    public DirectCurrentSynchronizedStartConfirmationRequest? LastSynchronizedStartRequest { get; private set; }

    public int ChannelStartConfirmationCount { get; private set; }

    public int SynchronizedStartConfirmationCount { get; private set; }

    public Func<DirectCurrentChannelStartConfirmationRequest, bool>? ChannelStartConfirmationHandler { get; set; }

    public Func<DirectCurrentSynchronizedStartConfirmationRequest, bool>? SynchronizedStartConfirmationHandler { get; set; }

    public bool ConfirmWarning(string title, string message, string confirmText, string cancelText)
    {
        LastConfirmationTitle = title;
        LastConfirmationMessage = message;
        return ConfirmationResult;
    }

    public bool ConfirmDirectCurrentChannelStart(DirectCurrentChannelStartConfirmationRequest request)
    {
        LastChannelStartRequest = request;
        ChannelStartConfirmationCount++;
        return ChannelStartConfirmationHandler?.Invoke(request) ?? ChannelStartConfirmationResult;
    }

    public bool ConfirmDirectCurrentSynchronizedStart(DirectCurrentSynchronizedStartConfirmationRequest request)
    {
        LastSynchronizedStartRequest = request;
        SynchronizedStartConfirmationCount++;
        return SynchronizedStartConfirmationHandler?.Invoke(request) ?? SynchronizedStartConfirmationResult;
    }

    public PasswordChangeDialogResult? RequestPasswordChange(string? errorMessage = null) => null;

    public void ShowInformation(string title, string message)
    {
    }

    public void ShowError(string title, string message)
    {
    }

    public Task<PrescriptionDefinition?> SelectStimulationPrescriptionAsync(
        string stimulationType,
        string applyScopeText,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<PrescriptionDefinition?>(null);
}
