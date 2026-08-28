using System.Windows;
using System.Windows.Controls;
using CircleToSearch.Ui;

namespace CircleToSearch.Settings;

public sealed class SettingsPanel : UserControl
{
    private const string PanelMarginResource = "SettingPanelMargin";
    private const string ItemMarginResource = "SettingPanelItemTopBottomMargin";

    public SettingsPanel(
        PluginSettings settings,
        Func<string, bool> applyHotkey,
        Action save,
        string? webView2Version,
        string providerDisplayName,
        UiStrings strings)
    {
        var searchMode = CreateText(strings.SearchProvider(providerDisplayName));
        var runtime = CreateText(strings.GoogleLensRuntime(webView2Version));
        var gestureLabel = CreateText(strings.SettingsHotkeyLabel);
        var gesture = CreateInput(settings.HotkeyGesture);

        var maxSideLabel = CreateText(strings.SettingsMaxImageSideLabel);
        var maxSide = CreateInput(settings.MaxLongSidePx.ToString());

        var status = CreateText(string.Empty);

        var apply = new Button { Content = strings.SettingsApply };
        apply.Click += (_, _) =>
        {
            try
            {
                settings.HotkeyGesture = gesture.Text.Trim();
                if (int.TryParse(maxSide.Text, out var max) && max is >= 256 and <= 8000)
                    settings.MaxLongSidePx = max;
                save();
                status.Text = applyHotkey(settings.HotkeyGesture)
                    ? strings.HotkeyRegistered
                    : strings.HotkeyRegistrationFailed;
            }
            catch (Exception exception)
            {
                status.Text = strings.SavingFailed(exception.Message);
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
