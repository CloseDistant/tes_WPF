namespace RuinaoSoftwareWpf.Tests;

using System.Windows.Input;
using RuinaoSoftwareWpf.Views;
using Xunit;

public sealed class EegMarkerShortcutInputTests
{
    [Theory]
    [InlineData(Key.F8, "F8")]
    [InlineData(Key.F9, "F9")]
    [InlineData(Key.F10, "F10")]
    [InlineData(Key.F11, "F11")]
    [InlineData(Key.F12, "F12")]
    public void TryGetShortcutText_FunctionKey_ReturnsStableText(Key key, string expected)
    {
        var resolved = EegMarkerShortcutInput.TryGetShortcutText(key, Key.None, out var shortcut);

        Assert.True(resolved);
        Assert.Equal(expected, shortcut);
    }

    [Fact]
    public void TryGetShortcutText_SystemKey_UsesUnderlyingFunctionKey()
    {
        var resolved = EegMarkerShortcutInput.TryGetShortcutText(Key.System, Key.F10, out var shortcut);

        Assert.True(resolved);
        Assert.Equal("F10", shortcut);
    }

    [Theory]
    [InlineData(Key.None)]
    [InlineData(Key.Tab)]
    [InlineData(Key.LeftCtrl)]
    [InlineData(Key.RightShift)]
    public void TryGetShortcutText_UnsupportedKey_DoesNotCreateShortcut(Key key)
    {
        var resolved = EegMarkerShortcutInput.TryGetShortcutText(key, Key.None, out var shortcut);

        Assert.False(resolved);
        Assert.Empty(shortcut);
    }
}
