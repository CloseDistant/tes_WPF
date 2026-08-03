namespace RuinaoSoftwareWpf.Tests;

internal sealed class TestUserDialogService : IUserDialogService
{
    public bool ConfirmationResult { get; set; } = true;

    public string? LastConfirmationTitle { get; private set; }

    public string? LastConfirmationMessage { get; private set; }

    public bool ConfirmWarning(string title, string message, string confirmText, string cancelText)
    {
        LastConfirmationTitle = title;
        LastConfirmationMessage = message;
        return ConfirmationResult;
    }

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
