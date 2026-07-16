namespace RuinaoSoftwareWpf;

using System.Windows;

public enum ToastKind
{
    Information,
    Success,
    Warning,
    Error
}

public interface IToastService
{
    Visibility Visibility { get; }
    string Title { get; }
    string Message { get; }
    string Icon { get; }
    string Accent { get; }

    void Show(ToastKind kind, string title, string message, TimeSpan? duration = null);
    void ShowInformation(string message, string title = "提示");
    void ShowSuccess(string title, string message);
    void ShowError(string title, string message);
}
