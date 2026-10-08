using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace CircleToSearch.Sounds;

internal static class UiClickSounds
{
    // Buttons often mark their click handled, so the handler must see handled events too.
    internal static void Attach(UIElement root, Action<UiSound> play) =>
        root.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, e) => play(For(e.OriginalSource))),
            handledEventsToo: true);

    // ToggleButton flips IsChecked before raising Click, so the box already shows its new state.
    internal static UiSound For(object source) => source is CheckBox box
        ? box.IsChecked == true ? UiSound.SwitchOn : UiSound.SwitchOff
        : UiSound.Tap;
}
