using System.Windows;
using System.Windows.Controls;

namespace CircleToSearch.Settings;

public sealed class SettingsPanel : UserControl
{
    private const string PanelMarginResource = "SettingPanelMargin";
    private const string ItemMarginResource = "SettingPanelItemTopBottomMargin";

    public SettingsPanel(
        PluginSettings settings,
        Func<string, bool> applyHotkey,
        Action save,
        string? webView2Version)
    {
        var searchMode = CreateText("Search: Google Lens results in WebView2");
        var runtime = CreateText(webView2Version is null
            ? "WebView2 Runtime: not detected"
            : $"WebView2 Runtime: {webView2Version}");
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
                searchMode,
                runtime,
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
