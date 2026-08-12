using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RuinaoSoftwareWpf.Views;

public partial class EegExportDataDialog : Window
{
    private DirectoryInfo? currentDirectory;

    public EegExportDataDialog(string defaultDataName)
    {
        InitializeComponent();
        DataNameTextBox.Text = SanitizeFileName(defaultDataName);
        LoadDrives();
    }

    public string ExportFilePath { get; private set; } = string.Empty;

    private void LoadDrives()
    {
        DriveComboBox.Items.Clear();
        DriveComboBox.IsEnabled = true;
        foreach (var drive in DriveInfo.GetDrives().Where(item => item.IsReady && item.DriveType == DriveType.Removable))
        {
            DriveComboBox.Items.Add(new ComboBoxItem
            {
                Content = $"{drive.Name} {GetDriveDisplayName(drive)}",
                Tag = drive.RootDirectory.FullName,
                Style = (Style)FindResource("DarkComboBoxItem")
            });
        }

        if (DriveComboBox.Items.Count > 0)
        {
            DriveComboBox.SelectedIndex = 0;
        }
        else
        {
            DriveComboBox.IsEnabled = false;
            currentDirectory = null;
            SelectedFolderTextBox.Text = "未检测到 U 盘或可移动磁盘";
            FolderListPanel.Children.Clear();
            ErrorText.Text = "请插入 U 盘后导出";
        }
    }

    private void DriveComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if ((DriveComboBox.SelectedItem as ComboBoxItem)?.Tag is not string path)
        {
            return;
        }

        SetCurrentDirectory(path);
    }

    private void SetCurrentDirectory(string path)
    {
        try
        {
            currentDirectory = new DirectoryInfo(path);
            SelectedFolderTextBox.Text = currentDirectory.FullName;
            RenderFolders();
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private void RenderFolders()
    {
        FolderListPanel.Children.Clear();
        if (currentDirectory is null)
        {
            return;
        }

        if (currentDirectory.Parent is not null)
        {
            FolderListPanel.Children.Add(CreateFolderRow("..", currentDirectory.Parent.FullName, true));
        }

        DirectoryInfo[] folders;
        try
        {
            folders = currentDirectory.GetDirectories()
                .Where(item => (item.Attributes & FileAttributes.Hidden) == 0)
                .OrderBy(item => item.Name)
                .ToArray();
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            return;
        }

        foreach (var folder in folders)
        {
            FolderListPanel.Children.Add(CreateFolderRow(folder.Name, folder.FullName, false));
        }
    }

    private Border CreateFolderRow(string name, string path, bool isParent)
    {
        var row = new Border
        {
            Height = 34,
            Margin = new Thickness(6, 4, 6, 0),
            Background = new SolidColorBrush(Color.FromRgb(32, 39, 55)),
            Tag = path,
            Cursor = Cursors.Hand
        };
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };
        panel.Children.Add(new TextBlock
        {
            Text = isParent ? "↑" : "▣",
            Foreground = (Brush)FindResource(isParent ? "SubText" : "Gold"),
            FontSize = 14,
            Width = 24,
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = name,
            Foreground = (Brush)FindResource("Text"),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        });
        row.Child = panel;
        row.MouseLeftButtonDown += (_, _) => SetCurrentDirectory(path);
        return row;
    }

    private void CreateFolderButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (currentDirectory is null)
        {
            return;
        }

        var folderName = SanitizeFileName(NewFolderNameTextBox.Text.Trim());
        if (string.IsNullOrWhiteSpace(folderName))
        {
            ErrorText.Text = "请输入文件夹名称";
            return;
        }

        var target = Path.Combine(currentDirectory.FullName, folderName);
        if (Directory.Exists(target))
        {
            ErrorText.Text = "文件夹已存在";
            return;
        }

        Directory.CreateDirectory(target);
        NewFolderNameTextBox.Clear();
        SetCurrentDirectory(target);
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (currentDirectory is null)
        {
            ErrorText.Text = "请选择导出文件夹";
            return;
        }

        var dataName = SanitizeFileName(DataNameTextBox.Text.Trim());
        if (string.IsNullOrWhiteSpace(dataName))
        {
            ErrorText.Text = "请输入数据名称";
            return;
        }

        ExportFilePath = Path.Combine(currentDirectory.FullName, $"{dataName}.edf");
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private static string GetDriveDisplayName(DriveInfo drive)
    {
        return string.IsNullOrWhiteSpace(drive.VolumeLabel)
            ? drive.DriveType == DriveType.Removable ? "可移动磁盘" : "本地磁盘"
            : drive.VolumeLabel;
    }

    private static string SanitizeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = name.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars).Trim();
    }
}
