using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using CircleToSearch.Capture;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class OverlayShortcutsTests
{
    [Fact]
    public void Shortcut_text_is_built_from_the_definition_and_localized_key_names()
    {
        Assert.Equal("Enter", OverlayShortcuts.Search.Text(TestUiStrings.English));
        Assert.Equal("Ctrl+C", OverlayShortcuts.Copy.Text(TestUiStrings.English));
        Assert.Equal("4", OverlayShortcuts.MusicRecognition.Text(TestUiStrings.English));
    }

    [Theory]
    [InlineData(Key.D2, ModifierKeys.None, true)]
    [InlineData(Key.NumPad2, ModifierKeys.None, true)]
    [InlineData(Key.D2, ModifierKeys.Shift, true)]
    [InlineData(Key.D2, ModifierKeys.Control, false)]
    [InlineData(Key.D3, ModifierKeys.None, false)]
    public void Digit_shortcuts_accept_the_number_row_and_numpad(Key key, ModifierKeys modifiers, bool expected) =>
        Assert.Equal(expected, OverlayShortcuts.ProviderMenu.Matches(key, modifiers));

    [Fact]
    public void Modified_shortcuts_need_exactly_their_modifiers()
    {
        Assert.True(OverlayShortcuts.Copy.Matches(Key.C, ModifierKeys.Control));
        Assert.False(OverlayShortcuts.Copy.Matches(Key.C, ModifierKeys.None));
        Assert.False(OverlayShortcuts.Copy.Matches(Key.C, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(OverlayShortcuts.Search.Matches(Key.Enter, ModifierKeys.Control));
    }

    [Fact]
    public void Hint_follows_the_current_button_name_when_the_tooltip_opens()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var button = new Button();
                AutomationProperties.SetName(button, "Translate");
                OverlayShortcuts.Translate.AttachHint(button, TestUiStrings.English);
                Assert.Equal("Ctrl+T", AutomationProperties.GetAcceleratorKey(button));

                OpenToolTip(button);
                Assert.Equal("Translate (Ctrl+T)", button.ToolTip);

                AutomationProperties.SetName(button, "Show original");
                button.ToolTip = "Show original";
                OpenToolTip(button);
                Assert.Equal("Show original (Ctrl+T)", button.ToolTip);

                AutomationProperties.SetName(button, "Copied");
                KeyboardShortcut.SetShowsStatus(button, true);
                OpenToolTip(button);
                Assert.Equal("Copied", button.ToolTip);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }

    // ToolTipEventArgs has no public constructor; WPF raises it only for a real hover.
    private static void OpenToolTip(FrameworkElement element)
    {
        var args = (ToolTipEventArgs)Activator.CreateInstance(typeof(ToolTipEventArgs),
            BindingFlags.Instance | BindingFlags.NonPublic, null, [true], null)!;
        element.RaiseEvent(args);
    }
}
