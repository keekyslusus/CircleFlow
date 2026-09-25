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
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        parts.Add(key is >= Key.D0 and <= Key.D9 ? ((int)key - (int)Key.D0).ToString() : key.ToString());
        return string.Join('+', parts);
    }

    // Recording captures every key, but Alt+F4 must still close the window.
    public static bool ClosesWindow(KeyEventArgs e) => e.Key == Key.System && e.SystemKey == Key.F4;

    public static bool IsModifier(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift;

    public static IReadOnlyList<string> Labels(string gesture, UiStrings strings) =>
        gesture.Split('+').Select(token => token switch
        {
            "Ctrl" or "Alt" or "Shift" or "Space" or "Win" => strings.SettingsPreviewText(token.ToLowerInvariant()),
            _ => token,
        }).ToArray();

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
