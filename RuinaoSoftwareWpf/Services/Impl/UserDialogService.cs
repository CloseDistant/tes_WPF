namespace RuinaoSoftwareWpf;

using System.Windows;
using RuinaoSoftwareWpf.Views.Dialogs;

/// <summary>
/// WPF 弹窗服务实现。
/// 当前用于采集工作台的危险操作确认，后续其他模块也可以复用。
/// </summary>
public sealed class UserDialogService : IUserDialogService
{
    private readonly IPrescriptionService prescriptionService;

    public UserDialogService(IPrescriptionService prescriptionService)
    {
        this.prescriptionService = prescriptionService;
    }

    public bool ConfirmWarning(string title, string message, string confirmText, string cancelText)
    {
        var dialog = new WorkbenchConfirmDialog(title, message, confirmText, cancelText)
        {
            Owner = Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true;
    }

    public bool ConfirmDirectCurrentStart(DirectCurrentStartConfirmationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var dialog = new DirectCurrentStartConfirmationDialog(request)
        {
            Owner = Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true;
    }

    public void ShowInformation(string title, string message)
    {
        ShowMessageDialog(title, message, ThemedMessageKind.Information);
    }

    public void ShowError(string title, string message)
    {
        ShowMessageDialog(title, message, ThemedMessageKind.Error);
    }

    public async Task<PrescriptionDefinition?> SelectStimulationPrescriptionAsync(
        string stimulationType,
        string applyScopeText,
        CancellationToken cancellationToken = default)
    {
        var matching = new List<PrescriptionDefinition>();
        var offset = 0;
        PageResult<PrescriptionDefinition> page;
        do
        {
            page = await prescriptionService.GetPrescriptionsPageAsync(
                new PageRequest(offset, 100),
                cancellationToken);
            matching.AddRange(page.Items.Where(item =>
                string.Equals(item.StimulationType, stimulationType, StringComparison.Ordinal)));
            offset += page.Items.Count;
        }
        while (page.HasMore && page.Items.Count > 0);

        var dialog = new StimulationPrescriptionPickerDialog(
            stimulationType,
            applyScopeText,
            matching)
        {
            Owner = Application.Current?.MainWindow
        };

        return dialog.ShowDialog() == true ? dialog.SelectedPrescription : null;
    }

    private static void ShowMessageDialog(string title, string message, ThemedMessageKind kind)
    {
        var dialog = new ThemedMessageDialog(title, message, kind)
        {
            Owner = Application.Current?.MainWindow
        };
        dialog.ShowDialog();
    }
}
