namespace RuinaoSoftwareWpf.Views.Dialogs;

using System.Windows;
using System.ComponentModel;

public partial class OperationProgressDialog : Window
{
    private readonly CancellationTokenSource cancellation = new();
    private bool allowClose;

    public OperationProgressDialog(string title, bool canCancel = true)
    {
        InitializeComponent();
        TitleText.Text = title;
        CancelButton.Visibility = canCancel ? Visibility.Visible : Visibility.Collapsed;
    }

    public CancellationToken CancellationToken => cancellation.Token;

    public void Report(string stage, int percentage, string detail)
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            StageText.Text = stage;
            DetailText.Text = detail;
            OperationProgress.Value = Math.Clamp(percentage, 0, 100);
            PercentText.Text = $"{Math.Clamp(percentage, 0, 100)}%";
        });
    }

    public void Complete()
    {
        allowClose = true;
        if (IsVisible) Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        CancelButton.IsEnabled = false;
        StageText.Text = "正在取消";
        cancellation.Cancel();
    }

    protected override void OnClosed(EventArgs e)
    {
        cancellation.Dispose();
        base.OnClosed(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!allowClose)
        {
            e.Cancel = true;
            cancellation.Cancel();
        }
        base.OnClosing(e);
    }
}
