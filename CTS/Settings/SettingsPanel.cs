using System.Windows;
using System.Windows.Controls;

namespace CircleToSearch.Settings;

public sealed class SettingsPanel : UserControl
{
    private const string PanelMarginResource = "SettingPanelMargin";
    private const string ItemMarginResource = "SettingPanelItemTopBottomMargin";

    public SettingsPanel(PluginSettings settings, Func<string, bool> applyHotkey, Action save)
    {
        var modeLabel = CreateText("Search method");
        var mode = new ComboBox();
        mode.Items.Add("Paste into Google Lens (reliable)");
        mode.Items.Add("Fast upload with browser session (experimental, currently untrusted by Google)");
        mode.Items.Add("Direct upload to Lens (legacy, broken server-side)");
        mode.SelectedIndex = settings.SearchMode switch
        {
            PluginSettings.AutoMode => 1,
            PluginSettings.UploadMode => 2,
            _ => 0,
        };
        mode.SetResourceReference(MarginProperty, ItemMarginResource);

        var gestureLabel = CreateText("Hotkey (e.g. Ctrl+Alt+Space)");
        var gesture = CreateInput(settings.HotkeyGesture);

        var maxSideLabel = CreateText("Max image long side (px)");
        var maxSide = CreateInput(settings.MaxLongSidePx.ToString());

        var status = CreateText(string.Empty);

        var apply = new Button { Content = "Apply" };
        apply.Click += (_, _) =>
        {
            try
            {
                settings.SearchMode = mode.SelectedIndex switch
                {
                    1 => PluginSettings.AutoMode,
                    2 => PluginSettings.UploadMode,
                    _ => PluginSettings.PasteMode,
                };
                settings.HotkeyGesture = gesture.Text.Trim();
                if (int.TryParse(maxSide.Text, out var max) && max is >= 256 and <= 8000)
                    settings.MaxLongSidePx = max;
                save();
                status.Text = applyHotkey(settings.HotkeyGesture)
                    ? "Hotkey registered."
                    : "Hotkey was not registered — the combination may already be in use.";
            }
            catch (Exception exception)
            {
                status.Text = $"Saving failed: {exception.Message}";
            }
        };
        apply.SetResourceReference(MarginProperty, ItemMarginResource);

        var panel = new StackPanel
        {
            Children =
            {
                modeLabel,
                mode,
                gestureLabel,
                gesture,
                maxSideLabel,
                maxSide,
                apply,
                status,
            },
        };
        panel.SetResourceReference(MarginProperty, PanelMarginResource);
        Content = panel;
    }

    private static TextBlock CreateText(string text)
    {
        var textBlock = new TextBlock { Text = text };
        textBlock.SetResourceReference(MarginProperty, ItemMarginResource);
        return textBlock;
    }

    private static TextBox CreateInput(string text)
    {
        var input = new TextBox { Text = text };
        input.SetResourceReference(MarginProperty, ItemMarginResource);
        return input;
    }
}
