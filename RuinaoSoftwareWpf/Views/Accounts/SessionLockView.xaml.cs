namespace RuinaoSoftwareWpf.Views;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

public partial class SessionLockView : UserControl
{
    public SessionLockView()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
    }

    public event EventHandler? ExitRequested;

    public void PrepareForLock()
    {
        ClearPassword();
        if (DataContext is SessionLockViewModel viewModel)
        {
            viewModel.ClearMessage();
        }

        Dispatcher.BeginInvoke(new Action(() => PasswordBox.Focus()), DispatcherPriority.Input);
    }

    private string CurrentPassword => PasswordRevealButton.IsChecked == true
        ? VisiblePasswordBox.Text
        : PasswordBox.Password;

    private async void UnlockButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SessionLockViewModel viewModel)
        {
            return;
        }

        var result = await viewModel.UnlockAsync(CurrentPassword);
        ClearPassword();
        if (!result.Succeeded)
        {
            _ = Dispatcher.BeginInvoke(new Action(() => PasswordBox.Focus()), DispatcherPriority.Input);
        }
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
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
        PasswordRevealButton.Foreground = new SolidColorBrush(Color.FromRgb(145, 160, 182));
        PasswordBox.Focus();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            PrepareForLock();
        }
        else
        {
            ClearPassword();
        }
    }

    private void ClearPassword()
    {
        PasswordBox.Password = string.Empty;
        VisiblePasswordBox.Text = string.Empty;
        PasswordRevealButton.IsChecked = false;
    }
}
