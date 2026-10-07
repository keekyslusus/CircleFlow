using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using CircleToSearch.Ui;

namespace CircleToSearch.Capture;

// The single definition of each overlay key: handlers match against it and tooltips print it.
internal static class OverlayShortcuts
{
    internal static readonly OverlayShortcut Search = new(Key.Enter);
    internal static readonly OverlayShortcut Copy = new(Key.C, ModifierKeys.Control);
    internal static readonly OverlayShortcut Save = new(Key.S, ModifierKeys.Control);
    internal static readonly OverlayShortcut Translate = new(Key.T, ModifierKeys.Control);
    internal static readonly OverlayShortcut ProviderMenu = new(Key.D2);
    internal static readonly OverlayShortcut ScreenTranslation = new(Key.D3);
    internal static readonly OverlayShortcut MusicRecognition = new(Key.D4);
}

internal sealed record OverlayShortcut(Key Key, ModifierKeys Modifiers = ModifierKeys.None)
{
    internal bool Matches(Key key, ModifierKeys modifiers) =>
        Key is >= Key.D1 and <= Key.D9 && Modifiers == ModifierKeys.None
            ? KeyboardShortcut.Digit(key, modifiers) == Key - Key.D0
            : key == Key && modifiers == Modifiers;

    internal string Text(UiStrings strings) => ShortcutLabels.Text(ShortcutLabels.Gesture(Key, Modifiers), strings);

    // Composed from the accessible name each time the tooltip opens, so it follows every later relabeling of the
    // button (Translate / Show original, Recognize / Cancel) without each of those places knowing about the key.
    internal void AttachHint(ButtonBase button, UiStrings strings)
    {
        var keys = Text(strings);
        AutomationProperties.SetAcceleratorKey(button, keys);
        button.ToolTip ??= AutomationProperties.GetName(button);
        button.ToolTipOpening += (_, _) =>
        {
            var name = AutomationProperties.GetName(button);
            button.ToolTip = KeyboardShortcut.GetShowsStatus(button) ? name : strings.ShortcutHint(name, keys);
        };
    }
}

// A shortcut presses its button, so an action hidden in settings or disabled for the current state stays
// unavailable from the keyboard as well, and both paths share one handler.
internal static class KeyboardShortcut
{
    // While a button's name reports an outcome such as "Copied", it names no action, so its tooltip gets no key.
    internal static readonly DependencyProperty ShowsStatusProperty = DependencyProperty.RegisterAttached(
        "ShowsStatus", typeof(bool), typeof(KeyboardShortcut), new PropertyMetadata(false));

    internal static bool GetShowsStatus(DependencyObject element) => (bool)element.GetValue(ShowsStatusProperty);

    internal static void SetShowsStatus(DependencyObject element, bool value) =>
        element.SetValue(ShowsStatusProperty, value);

    internal static bool Press(ButtonBase? button)
    {
        if (button is not { IsVisible: true, IsEnabled: true }) return false;
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
        return true;
    }

    // Shift is allowed because layouts such as AZERTY type digits with it.
    internal static int? Digit(Key key, ModifierKeys modifiers)
    {
        if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) != 0) return null;
        return key switch
        {
            >= Key.D1 and <= Key.D9 => key - Key.D0,
            >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad0,
            _ => null,
        };
    }
}
