namespace RuinaoSoftwareWpf;

using System.Windows;

public sealed class AppToastService : ObservableObject, IToastService, IDisposable
{
    private Visibility visibility = Visibility.Collapsed;
    private string title = string.Empty;
    private string message = string.Empty;
    private string icon = "i";
    private string accent = "#D6A04C";
    private CancellationTokenSource? dismissCts;

    public Visibility Visibility { get => visibility; private set => SetProperty(ref visibility, value); }
    public string Title { get => title; private set => SetProperty(ref title, value); }
    public string Message { get => message; private set => SetProperty(ref message, value); }
    public string Icon { get => icon; private set => SetProperty(ref icon, value); }
    public string Accent { get => accent; private set => SetProperty(ref accent, value); }

    public void Show(ToastKind kind, string title, string message, TimeSpan? duration = null)
    {
        dismissCts?.Cancel();
        dismissCts?.Dispose();
        dismissCts = new CancellationTokenSource();

        (Icon, Accent) = kind switch
        {
            ToastKind.Success => ("✓", "#56D981"),
            ToastKind.Warning => ("!", "#E6A23C"),
            ToastKind.Error => ("!", "#FF626B"),
            _ => ("i", "#D6A04C")
        };
        Title = title;
        Message = message;
        Visibility = Visibility.Visible;
        _ = HideAfterDelayAsync(dismissCts, duration ?? TimeSpan.FromSeconds(3));
    }

    public void ShowInformation(string message, string title = "提示") => Show(ToastKind.Information, title, message);

    public void ShowSuccess(string title, string message) => Show(ToastKind.Success, title, message);

    public void ShowError(string title, string message) => Show(ToastKind.Error, title, message);

    public void Dispose()
    {
        dismissCts?.Cancel();
        dismissCts?.Dispose();
        dismissCts = null;
    }

    private async Task HideAfterDelayAsync(CancellationTokenSource owner, TimeSpan duration)
    {
        try
        {
            await Task.Delay(duration, owner.Token);
            if (ReferenceEquals(dismissCts, owner))
            {
                Visibility = Visibility.Collapsed;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(dismissCts, owner))
            {
                dismissCts = null;
                owner.Dispose();
            }
        }
    }
}
