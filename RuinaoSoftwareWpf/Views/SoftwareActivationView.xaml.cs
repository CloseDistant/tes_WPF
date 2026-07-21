namespace RuinaoSoftwareWpf.Views;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

public sealed class SoftwareActivationRequestedEventArgs : EventArgs
{
    public SoftwareActivationRequestedEventArgs(string activationCode)
    {
        ActivationCode = activationCode;
    }

    public string ActivationCode { get; }
}

public partial class SoftwareActivationView : UserControl
{
    private static readonly SolidColorBrush HintBrush = new(Color.FromRgb(142, 153, 171));
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(232, 78, 79));
    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(93, 218, 119));

    public SoftwareActivationView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                FocusActivationCode();
            }
        };
    }

    public event EventHandler<SoftwareActivationRequestedEventArgs>? ActivationRequested;

    public event EventHandler? ExitRequested;

    public void SetBusy(bool isBusy)
    {
        ActivationCodeBox.IsEnabled = !isBusy;
        CancelButton.IsEnabled = !isBusy;
        ActivateButton.IsEnabled = !isBusy;
        ActivateButton.Content = isBusy ? "激活中..." : "激活";
    }

    public void ShowMessage(string message, bool isError)
    {
        ActivationMessageText.Foreground = isError ? ErrorBrush : SuccessBrush;
        ActivationMessageText.Text = message;
    }

    public void ResetMessage()
    {
        ActivationMessageText.Foreground = HintBrush;
        ActivationMessageText.Text = "首次使用需要完成软件激活。";
    }

    public void ClearActivationCode()
    {
        ActivationCodeBox.Clear();
    }

    public void FocusActivationCode()
    {
        Dispatcher.BeginInvoke(new Action(() => ActivationCodeBox.Focus()), DispatcherPriority.Input);
    }

    public void ShowExitConfirmation()
    {
        ExitConfirmationOverlay.Visibility = Visibility.Visible;
    }

    private void ActivateButton_Click(object sender, RoutedEventArgs e)
    {
        RequestActivation();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        ExitConfirmationOverlay.Visibility = Visibility.Visible;
    }

    private void ContinueActivationButton_Click(object sender, RoutedEventArgs e)
    {
        ExitConfirmationOverlay.Visibility = Visibility.Collapsed;
        FocusActivationCode();
    }

    private void ExitSoftwareButton_Click(object sender, RoutedEventArgs e)
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ActivationCodeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        PlaceholderText.Visibility = string.IsNullOrEmpty(ActivationCodeBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ExitConfirmationOverlay.Visibility = ExitConfirmationOverlay.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        if (ExitConfirmationOverlay.Visibility == Visibility.Visible)
        {
            ExitConfirmationOverlay.Visibility = Visibility.Collapsed;
            FocusActivationCode();
        }
        else if (ActivateButton.IsEnabled)
        {
            RequestActivation();
        }

        e.Handled = true;
    }

    private void RequestActivation()
    {
        ResetMessage();
        ActivationRequested?.Invoke(
            this,
            new SoftwareActivationRequestedEventArgs(ActivationCodeBox.Text));
    }
}
