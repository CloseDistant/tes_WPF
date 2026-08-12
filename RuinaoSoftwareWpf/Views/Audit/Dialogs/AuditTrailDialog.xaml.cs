namespace RuinaoSoftwareWpf.Views.Dialogs;

using Microsoft.Win32;
using System.Windows;
using System.Windows.Input;

public partial class AuditTrailDialog : Window
{
    private readonly AuditTrailViewModel viewModel;

    public AuditTrailDialog(AuditTrailViewModel viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += AuditTrailDialog_Loaded;
    }

    private async void AuditTrailDialog_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= AuditTrailDialog_Loaded;
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(1300, Math.Max(MinWidth, workArea.Width - 40));
        Height = Math.Min(760, Math.Max(MinHeight, workArea.Height - 40));
        try
        {
            await viewModel.InitializeAsync();
        }
        catch (Exception exception)
        {
            ShowMessage("安全审计初始化失败", exception.Message, ThemedMessageKind.Error);
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出安全审计记录",
            Filter = "CSV文件 (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = $"安全审计_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var result = await viewModel.ExportAsync(dialog.FileName);
            ShowMessage(
                "导出完成",
                $"已导出 {result.ExportedCount:N0} 条审计记录。\n\nSHA-256：\n{result.Sha256}",
                ThemedMessageKind.Information);
        }
        catch (Exception exception)
        {
            ShowMessage("导出失败", exception.Message, ThemedMessageKind.Error);
        }
    }

    private void ShowMessage(string title, string message, ThemedMessageKind kind)
    {
        var dialog = new ThemedMessageDialog(title, message, kind)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }
}
