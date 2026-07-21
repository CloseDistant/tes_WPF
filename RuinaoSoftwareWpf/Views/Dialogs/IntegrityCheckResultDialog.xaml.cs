namespace RuinaoSoftwareWpf.Views.Dialogs;

using System.Reflection;
using System.Windows;
using System.Windows.Media;

public partial class IntegrityCheckResultDialog : Window
{
    public IntegrityCheckResultDialog(IntegrityCheckResult result)
    {
        InitializeComponent();
        ConfigureState(result);
        ConfigureReleaseResult(result);
    }

    private void ConfigureState(IntegrityCheckResult result)
    {
        HeaderText.Text = result.IsValid ? "校验完成" : "校验失败";
        CompletedAtText.Text = result.CompletedAt.ToString("yyyy-MM-dd HH:mm");
        if (result.IsValid) return;

        var error = new SolidColorBrush(Color.FromRgb(255, 105, 114));
        ResultIconBorder.BorderBrush = error;
        ResultIconText.Foreground = error;
        ResultIconText.Text = "!";
        FirstValueText.Foreground = error;
        SecondValueText.Foreground = error;
        CompletedAtText.Foreground = new SolidColorBrush(Color.FromRgb(205, 213, 225));
    }

    private void ConfigureReleaseResult(IntegrityCheckResult result)
    {
        ResultTitleText.Text = result.IsValid ? "软件发布文件完整性校验通过" : "软件发布文件完整性校验失败";
        ResultDetailText.Text = result.IsValid ? "未发现文件缺失、替换或内容修改。" : result.Message;
        FirstLabelText.Text = "软件版本";
        FirstDescriptionText.Visibility = Visibility.Collapsed;
        FirstValueText.Text = $"V{GetSoftwareVersion()}";
        SecondLabelText.Text = "校验文件";
        SecondDescriptionText.Visibility = Visibility.Collapsed;
        SecondValueText.Text = result.IsValid ? $"{result.VerifiedCount:N0} 个" : "未通过";
    }

    private static string GetSoftwareVersion()
    {
        return typeof(IntegrityCheckResultDialog).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "1.0.0";
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
