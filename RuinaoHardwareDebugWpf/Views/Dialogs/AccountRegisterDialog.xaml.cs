namespace RuinaoHardwareDebugWpf.Views.Dialogs;

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

public partial class AccountRegisterDialog : Window
{
    private int selectedRoleId = AccountRoles.Doctor;

    public AccountRegisterDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => LoginNameBox.Focus();
    }

    public CreateAccountRequest Request => new(
        LoginNameBox.Text.Trim(),
        PasswordBox.Password,
        ConfirmPasswordBox.Password,
        DisplayNameBox.Text.Trim(),
        SelectedRoleId);

    public string ErrorMessage
    {
        get => ErrorText.Text;
        set => ErrorText.Text = value;
    }

    private int SelectedRoleId => selectedRoleId;

    private void RegisterButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void DoctorRoleButton_Click(object sender, RoutedEventArgs e)
    {
        SelectRole(AccountRoles.Doctor);
    }

    private void TechnicianRoleButton_Click(object sender, RoutedEventArgs e)
    {
        SelectRole(AccountRoles.Technician);
    }

    private void SelectRole(int roleId)
    {
        selectedRoleId = roleId;
        ApplyRoleButtonState();
    }

    private void ApplyRoleButtonState()
    {
        var selectedBackground = new SolidColorBrush(Color.FromRgb(208, 144, 62));
        var selectedBorder = new SolidColorBrush(Color.FromRgb(208, 144, 62));
        var normalBackground = new SolidColorBrush(Color.FromRgb(49, 54, 68));
        var normalBorder = new SolidColorBrush(Color.FromRgb(59, 65, 80));
        var normalForeground = new SolidColorBrush(Color.FromRgb(200, 208, 222));

        DoctorRoleButton.Background = selectedRoleId == AccountRoles.Doctor ? selectedBackground : normalBackground;
        DoctorRoleButton.BorderBrush = selectedRoleId == AccountRoles.Doctor ? selectedBorder : normalBorder;
        DoctorRoleButton.Foreground = selectedRoleId == AccountRoles.Doctor ? Brushes.White : normalForeground;

        TechnicianRoleButton.Background = selectedRoleId == AccountRoles.Technician ? selectedBackground : normalBackground;
        TechnicianRoleButton.BorderBrush = selectedRoleId == AccountRoles.Technician ? selectedBorder : normalBorder;
        TechnicianRoleButton.Foreground = selectedRoleId == AccountRoles.Technician ? Brushes.White : normalForeground;
    }

    private void DialogRoot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
