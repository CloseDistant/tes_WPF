namespace RuinaoSoftwareWpf.Views;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

public sealed class LoginRequestedEventArgs : EventArgs
{
    public LoginRequestedEventArgs(string loginName, string password, bool rememberAccount)
    {
        LoginName = loginName;
        Password = password;
        RememberAccount = rememberAccount;
    }

    public string LoginName { get; }

    public string Password { get; }

    public bool RememberAccount { get; }
}

public partial class LoginView : UserControl
{
    private readonly DispatcherTimer clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public LoginView()
    {
        InitializeComponent();
        clockTimer.Tick += (_, _) => UpdateClock();
        Loaded += (_, _) =>
        {
            UpdateClock();
            clockTimer.Start();
        };
        Unloaded += (_, _) => clockTimer.Stop();
    }

    public event EventHandler<LoginRequestedEventArgs>? LoginRequested;

    public event EventHandler? ExitRequested;

    public void ApplyRememberedLoginName(string? loginName)
    {
        LoginNameBox.Text = loginName ?? string.Empty;
        RememberAccountCheckBox.IsChecked = !string.IsNullOrWhiteSpace(loginName) || RememberAccountCheckBox.IsChecked == true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (string.IsNullOrWhiteSpace(LoginNameBox.Text))
            {
                LoginNameBox.Focus();
            }
            else
            {
                PasswordBox.Focus();
            }
        }), DispatcherPriority.Input);
    }

    public void SetBusy(bool isBusy)
    {
        LoginNameBox.IsEnabled = !isBusy;
        PasswordBox.IsEnabled = !isBusy;
        VisiblePasswordBox.IsEnabled = !isBusy;
        PasswordRevealButton.IsEnabled = !isBusy;
        RememberAccountCheckBox.IsEnabled = !isBusy;
        LoginButtonControl.IsEnabled = !isBusy;
        LoginButtonControl.Content = isBusy ? "登录中..." : "登录";
    }

    public void ShowMessage(string message, bool isError)
    {
        MessageText.Foreground = new SolidColorBrush(isError
            ? Color.FromRgb(232, 78, 79)
            : Color.FromRgb(93, 218, 119));
        MessageText.Text = message;
    }

    public void ClearPassword()
    {
        PasswordBox.Password = string.Empty;
        VisiblePasswordBox.Text = string.Empty;
        PasswordRevealButton.IsChecked = false;
    }

    private string CurrentPassword => PasswordRevealButton.IsChecked == true
        ? VisiblePasswordBox.Text
        : PasswordBox.Password;

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        MessageText.Text = string.Empty;
        LoginRequested?.Invoke(this, new LoginRequestedEventArgs(
            LoginNameBox.Text.Trim(),
            CurrentPassword,
            RememberAccountCheckBox.IsChecked == true));
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        LoginMoreToggle.IsChecked = false;
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    private void PasswordRevealButton_Checked(object sender, RoutedEventArgs e)
    {
        VisiblePasswordBox.Text = PasswordBox.Password;
        PasswordBox.Visibility = Visibility.Collapsed;
        VisiblePasswordBox.Visibility = Visibility.Visible;
        PasswordRevealButton.Foreground = new SolidColorBrush(Color.FromRgb(208, 144, 62));
        VisiblePasswordBox.Focus();
        VisiblePasswordBox.CaretIndex = VisiblePasswordBox.Text.Length;
    }

    private void PasswordRevealButton_Unchecked(object sender, RoutedEventArgs e)
    {
        PasswordBox.Password = VisiblePasswordBox.Text;
        VisiblePasswordBox.Visibility = Visibility.Collapsed;
        PasswordBox.Visibility = Visibility.Visible;
        PasswordRevealButton.Foreground = new SolidColorBrush(Color.FromRgb(142, 150, 168));
        PasswordBox.Focus();
    }

    private void UpdateClock()
    {
        ClockText.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }
}
