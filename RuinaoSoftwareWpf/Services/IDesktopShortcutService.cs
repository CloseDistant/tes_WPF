namespace RuinaoSoftwareWpf;

public sealed record DesktopShortcutResult(
    bool Succeeded,
    string Message,
    string? ShortcutPath = null);

public interface IDesktopShortcutService
{
    DesktopShortcutResult CreateOrUpdate();
}
