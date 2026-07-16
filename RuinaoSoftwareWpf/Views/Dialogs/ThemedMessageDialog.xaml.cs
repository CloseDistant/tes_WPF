namespace RuinaoSoftwareWpf.Views.Dialogs;

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

public enum ThemedMessageKind
{
    Information,
    Error
}

public partial class ThemedMessageDialog : Window
{
    public ThemedMessageDialog(string title, string message, ThemedMessageKind kind)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;

        if (kind == ThemedMessageKind.Error)
        {
            CategoryText.Text = "错误提示";
            IconText.Text = "!";
            IconText.FontStyle = FontStyles.Normal;
            IconText.Foreground = new SolidColorBrush(Color.FromRgb(255, 158, 158));
            IconBorder.Background = new SolidColorBrush(Color.FromRgb(62, 34, 39));
            IconBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(219, 91, 103));
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void DialogRoot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
