namespace RuinaoSoftwareWpf.Views.Dialogs;

using System.Windows;
using System.Windows.Input;

public partial class ChangePasswordDialog : Window
{
    public ChangePasswordDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => PasswordBox.Focus();
    }

    public string NewPassword => PasswordBox.Password;

    public string ConfirmPassword => ConfirmPasswordBox.Password;

    public string ErrorMessage
    {
        get => ErrorText.Text;
        set => ErrorText.Text = value;
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
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

