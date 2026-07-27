using RuinaoSoftwareWpf.Views.Dialogs;
using System.Windows;
using System.Windows.Controls;

namespace RuinaoSoftwareWpf.Views;

/// <summary>
/// 关于页面视图（同时承载管理员功能显示配置）。
/// XAML 负责布局和样式，这个 Code-Behind 文件只调用 InitializeComponent，逻辑交给 ConfigViewModel。
/// </summary>
public partial class ConfigView : UserControl
{
    private System.Windows.Window? ownerWindow;

    public ConfigView()
    {
        InitializeComponent();
    }

    private async void ConfigView_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        DetachWindowKeyHandler();
        ownerWindow = System.Windows.Window.GetWindow(this);
        if (ownerWindow is not null)
        {
            ownerWindow.PreviewKeyDown += ConfigView_PreviewKeyDown;
        }

        Focus();
        System.Windows.Input.Keyboard.Focus(this);
        await RefreshBackupStatusAsync();
    }

    private void ConfigView_Unloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        DetachWindowKeyHandler();
    }

    private void ConfigView_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (DataContext is not ConfigViewModel viewModel || e.IsRepeat)
        {
            return;
        }

        if (e.Key is System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift)
        {
            viewModel.RegisterShiftPress(DateTimeOffset.Now);
            return;
        }

        viewModel.ResetShiftSequence();
    }

    private void DetachWindowKeyHandler()
    {
        if (ownerWindow is null)
        {
            return;
        }

        ownerWindow.PreviewKeyDown -= ConfigView_PreviewKeyDown;
        ownerWindow = null;
    }

    private async void ReleaseIntegrityButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ConfigViewModel viewModel)
        {
            return;
        }

        var progressDialog = new OperationProgressDialog("发布文件完整性校验") { Owner = Window.GetWindow(this) };
        var progress = new Progress<IntegrityCheckProgress>(item =>
            progressDialog.Report(item.Stage, item.Percentage, item.CurrentItem));
        try
        {
            var operation = viewModel.CheckReleaseFilesAsync(progress, progressDialog.CancellationToken);
            CloseWhenCompleted(progressDialog, operation);
            progressDialog.ShowDialog();
            var result = await operation;
            new IntegrityCheckResultDialog(result)
            {
                Owner = Window.GetWindow(this)
            }.ShowDialog();
        }
        catch (OperationCanceledException)
        {
            viewModel.NotifyIntegrityCheckCanceled();
        }
        catch (Exception exception)
        {
            new ThemedMessageDialog("校验失败", exception.Message, ThemedMessageKind.Error)
            {
                Owner = Window.GetWindow(this)
            }.ShowDialog();
        }
    }

    private async void CreateBackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ConfigViewModel viewModel)
        {
            return;
        }

        try
        {
            var location = await viewModel.GetDefaultBackupLocationAsync();
            var request = new BackupRequestDialog(false, location) { Owner = Window.GetWindow(this) };
            if (request.ShowDialog() != true) return;

            var progressDialog = new OperationProgressDialog("创建数据备份") { Owner = Window.GetWindow(this) };
            var progress = new Progress<BackupOperationProgress>(item =>
                progressDialog.Report(item.Stage, item.Percentage, item.Detail));
            var operation = viewModel.CreateBackupAsync(
                request.SelectedPath,
                request.Password,
                progress,
                progressDialog.CancellationToken);
            CloseWhenCompleted(progressDialog, operation);
            progressDialog.ShowDialog();
            var result = await operation;
            if (result.Succeeded)
            {
                viewModel.NotifyBackupSucceeded(result.FilePath);
                await RefreshBackupStatusAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            viewModel.NotifyBackupFailed(exception);
        }
    }

    private async void RestoreDataButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ConfigViewModel viewModel)
        {
            return;
        }

        try
        {
            var request = new BackupRequestDialog(true) { Owner = Window.GetWindow(this) };
            if (request.ShowDialog() != true) return;

            if (!viewModel.ConfirmDataRestore())
            {
                return;
            }

            var progressDialog = new OperationProgressDialog("恢复数据", canCancel: false) { Owner = Window.GetWindow(this) };
            var progress = new Progress<BackupOperationProgress>(item =>
                progressDialog.Report(item.Stage, item.Percentage, item.Detail));
            var operation = viewModel.RestoreBackupAsync(request.SelectedPath, request.Password, progress);
            CloseWhenCompleted(progressDialog, operation);
            progressDialog.ShowDialog();
            var result = await operation;
            if (result.Succeeded)
            {
                new RestoreCompletedDialog { Owner = Window.GetWindow(this) }.ShowDialog();
            }
        }
        catch (DataRestoreRequiresExitException exception)
        {
            new RestoreCompletedDialog("数据恢复失败", exception.Message)
            {
                Owner = Window.GetWindow(this)
            }.ShowDialog();
        }
        catch (Exception exception)
        {
            viewModel.NotifyRestoreFailed(exception);
        }
    }

    private async Task RefreshBackupStatusAsync()
    {
        if (DataContext is not ConfigViewModel viewModel)
        {
            return;
        }

        try
        {
            var status = await viewModel.GetBackupStatusAsync();
            LastBackupAtText.Text = status.LastBackupAt?.ToString("yyyy-MM-dd HH:mm") ?? "尚未备份";
            LastBackupFileText.Text = status.LastBackupFileName ?? "--";
        }
        catch
        {
            LastBackupAtText.Text = "尚未备份";
            LastBackupFileText.Text = "--";
        }
    }

    private static void CloseWhenCompleted<T>(OperationProgressDialog dialog, Task<T> operation)
    {
        _ = operation.ContinueWith(
            _ => dialog.Dispatcher.BeginInvoke(dialog.Complete),
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
    }
}
