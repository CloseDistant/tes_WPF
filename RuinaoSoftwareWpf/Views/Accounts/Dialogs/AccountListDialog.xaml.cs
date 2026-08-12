namespace RuinaoSoftwareWpf.Views.Dialogs;

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

public partial class AccountListDialog : Window
{
    private const int PageSize = 30;
    private readonly IAccountService accountService;
    private readonly ObservableCollection<AccountListRow> rows = [];
    private int nextOffset;
    private bool hasMore = true;
    private bool isLoading;

    public AccountListDialog(IAccountService accountService)
    {
        this.accountService = accountService;
        InitializeComponent();
        AccountListBox.ItemsSource = rows;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(93, 218, 119));
        StatusText.Text = string.Empty;

        try
        {
            rows.Clear();
            nextOffset = 0;
            hasMore = true;
            await LoadMoreAsync();
            EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            rows.Clear();
            EmptyText.Visibility = Visibility.Collapsed;
            ShowError(ex.Message);
        }
    }

    private async Task LoadMoreAsync()
    {
        if (isLoading || !hasMore)
        {
            return;
        }

        isLoading = true;
        try
        {
            var page = await accountService.GetAccountListPageAsync(new PageRequest(nextOffset, PageSize));
            foreach (var item in page.Items)
            {
                rows.Add(new AccountListRow(
                item.UserId,
                item.LoginName,
                item.DisplayName,
                item.RoleName,
                item.IsActive,
                item.IsActive ? "已启用" : "已停用",
                item.IsActive ? "#5DDA77" : "#8A94A6",
                DateTimeOffset.FromUnixTimeMilliseconds(item.CreatedAtUnixMs).LocalDateTime.ToString("yyyy-MM-dd HH:mm")));
            }

            nextOffset += page.Items.Count;
            hasMore = page.HasMore;
            EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            isLoading = false;
        }
    }

    private async void AccountListBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.VerticalChange == 0 || e.VerticalOffset < e.ExtentHeight - e.ViewportHeight - 2)
        {
            return;
        }

        await LoadMoreAsync();
    }

    private async void ResetPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: AccountListRow account })
        {
            return;
        }

        string? error = null;
        while (true)
        {
            var dialog = new ResetPasswordDialog(account.LoginName)
            {
                Owner = this,
                ErrorMessage = error ?? string.Empty
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                await accountService.ResetPasswordAsync(new ResetPasswordRequest(
                    account.UserId,
                    dialog.NewPassword,
                    dialog.ConfirmPassword));

                if (!accountService.IsCurrentUserAdmin())
                {
                    Close();
                    return;
                }

                StatusText.Text = $"已重置账号 {account.LoginName} 的密码。";
                await RefreshAsync();
                StatusText.Text = $"已重置账号 {account.LoginName} 的密码。";
                return;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
        }
    }

    private void ShowError(string message)
    {
        StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(227, 106, 106));
        StatusText.Text = message;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private sealed record AccountListRow(
        long UserId,
        string LoginName,
        string DisplayName,
        string RoleName,
        bool IsActive,
        string StatusText,
        string StatusForeground,
        string CreatedAtText);
}
