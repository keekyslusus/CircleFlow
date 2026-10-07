using System.Globalization;
using System.Windows.Input;

namespace CircleToSearch.Ui;

// A gesture such as "Ctrl+Alt+S" is the stored, culture-neutral form; labels are how it reads in the UI language.
internal static class ShortcutLabels
{
    internal static string Gesture(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        parts.Add(key switch
        {
            >= Key.D0 and <= Key.D9 => (key - Key.D0).ToString(CultureInfo.InvariantCulture),
            Key.Enter => "Enter",
            _ => key.ToString(),
        });
        return string.Join('+', parts);
    }

    internal static IReadOnlyList<string> For(string gesture, UiStrings strings) =>
        gesture.Split('+').Select(token => token switch
        {
            "Ctrl" or "Alt" or "Shift" or "Space" or "Win" or "Enter" =>
                strings.SettingsPreviewText(token.ToLowerInvariant()),
            _ => token,
        }).ToArray();

    internal static string Text(string gesture, UiStrings strings) =>
        string.Join(strings.SettingsPreviewText("plus"), For(gesture, strings));
}
