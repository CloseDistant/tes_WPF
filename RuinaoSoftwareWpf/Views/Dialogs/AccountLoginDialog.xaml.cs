namespace RuinaoSoftwareWpf.Views.Dialogs;

using System.Windows;
using System.Windows.Input;

public partial class AccountLoginDialog : Window
{
    public AccountLoginDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => LoginNameBox.Focus();
    }

    public string LoginName => LoginNameBox.Text.Trim();

    public string Password => PasswordBox.Password;

    public void ShowError(string message)
    {
        ErrorText.Text = message;
    }

    private void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void DialogRoot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}

