namespace RuinaoSoftwareWpf.Views.Dialogs;

using System.Windows;
using System.ComponentModel;

public partial class RestoreCompletedDialog : Window
{
    private bool exitRequested;

    public RestoreCompletedDialog(string? title = null, string? message = null)
    {
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(title))
        {
            RestoreTitleText.Text = title;
        }
        if (!string.IsNullOrWhiteSpace(message))
        {
            RestoreMessageText.Text = message;
        }
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        exitRequested = true;
        Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!exitRequested) e.Cancel = true;
        base.OnClosing(e);
    }
}
