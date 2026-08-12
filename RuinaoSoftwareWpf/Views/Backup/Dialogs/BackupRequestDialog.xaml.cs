namespace RuinaoSoftwareWpf.Views.Dialogs;

using Microsoft.Win32;
using System.IO;
using System.Windows;

public partial class BackupRequestDialog : Window
{
    private readonly bool restoreMode;

    public BackupRequestDialog(bool restoreMode, BackupLocationInfo? location = null, string? restoreFile = null)
    {
        InitializeComponent();
        this.restoreMode = restoreMode;
        TitleText.Text = restoreMode ? "恢复数据" : "创建数据备份";
        PathLabel.Text = restoreMode ? "备份文件" : "保存位置";
        ConfirmButton.Content = restoreMode ? "恢复数据" : "创建备份";
        ConfirmPasswordPanel.Visibility = restoreMode ? Visibility.Collapsed : Visibility.Visible;
        SpaceGrid.Visibility = restoreMode ? Visibility.Collapsed : Visibility.Visible;
        PathText.Text = restoreFile ?? location?.DirectoryPath ?? string.Empty;
        LocationMessageText.Text = location?.Message ?? string.Empty;
        if (location is not null)
        {
            EstimateText.Text = $"预计大小  {FormatSize(location.EstimatedBytes)}";
            AvailableText.Text = location.AvailableBytes > 0 ? $"可用空间  {FormatSize(location.AvailableBytes)}" : string.Empty;
        }
    }

    public string SelectedPath => PathText.Text;
    public string Password => PasswordInput.Password;

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        if (restoreMode)
        {
            var dialog = new OpenFileDialog { Filter = "睿脑数据备份 (*.rnbak)|*.rnbak", CheckFileExists = true };
            if (dialog.ShowDialog(this) == true) PathText.Text = dialog.FileName;
            return;
        }

        var folder = new OpenFolderDialog { Title = "选择数据备份保存位置" };
        if (folder.ShowDialog(this) == true)
        {
            PathText.Text = folder.FolderName;
            LocationMessageText.Text = string.Empty;
            try
            {
                AvailableText.Text = $"可用空间  {FormatSize(new DriveInfo(Path.GetPathRoot(folder.FolderName)!).AvailableFreeSpace)}";
            }
            catch { AvailableText.Text = string.Empty; }
        }
    }

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PathText.Text))
        {
            ValidationText.Text = restoreMode ? "请选择备份文件" : "请选择保存位置";
            return;
        }
        if (PasswordInput.Password.Length < 8)
        {
            ValidationText.Text = "备份密码至少需要8位字符";
            return;
        }
        if (!restoreMode && PasswordInput.Password != ConfirmPasswordInput.Password)
        {
            ValidationText.Text = "两次输入的备份密码不一致";
            return;
        }
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private static string FormatSize(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / (1024d * 1024 * 1024):0.0} GB"
        : $"{bytes / (1024d * 1024):0.0} MB";
}
