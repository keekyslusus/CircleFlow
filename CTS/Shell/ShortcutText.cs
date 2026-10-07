using System.Windows.Input;
using CircleToSearch.Settings;
using CircleToSearch.Ui;

namespace CircleToSearch.Shell;

internal static class ShortcutText
{
    public static string? Gesture(Key key, ModifierKeys modifiers)
    {
        var validKey = key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or Key.Space;
        if (!validKey || modifiers == ModifierKeys.None || modifiers.HasFlag(ModifierKeys.Windows)) return null;
        return ShortcutLabels.Gesture(key, modifiers);
    }

    // Recording captures every key, but Alt+F4 must still close the window.
    public static bool ClosesWindow(KeyEventArgs e) => e.Key == Key.System && e.SystemKey == Key.F4;

    public static bool IsModifier(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift;

    public static ShortcutKey[] Keys(string gesture, UiStrings strings)
    {
        var plus = strings.SettingsPreviewText("plus");
        return ShortcutLabels.For(gesture, strings).Select((label, index) => new ShortcutKey(label, index > 0, plus)).ToArray();
    }

    public static string ChangeMessage(SettingsChangeResult result, string gesture, string success, UiStrings strings) =>
        result.Status switch
        {
            SettingsChangeStatus.Success => success,
            SettingsChangeStatus.Invalid => strings.SettingsShortcutInvalid,
            SettingsChangeStatus.HotkeyUnavailable => strings.SettingsShortcutUnavailable(gesture),
            SettingsChangeStatus.HotkeyRollbackFailed => strings.HotkeyRollbackFailed,
            _ => strings.StorageSaveFailed,
        };
}

internal sealed record ShortcutKey(string Label, bool HasSeparator, string Plus);
