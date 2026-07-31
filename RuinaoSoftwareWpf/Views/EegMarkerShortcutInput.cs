using System.Windows.Input;

namespace RuinaoSoftwareWpf.Views;

internal static class EegMarkerShortcutInput
{
    public static bool TryGetShortcutText(Key key, Key systemKey, out string shortcut)
    {
        var effectiveKey = key == Key.System ? systemKey : key;
        if (effectiveKey is Key.None
            or Key.Tab
            or Key.LeftCtrl
            or Key.RightCtrl
            or Key.LeftShift
            or Key.RightShift
            or Key.LeftAlt
            or Key.RightAlt)
        {
            shortcut = string.Empty;
            return false;
        }

        shortcut = effectiveKey.ToString();
        return true;
    }
}
