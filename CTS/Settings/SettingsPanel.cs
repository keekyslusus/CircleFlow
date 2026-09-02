using System.Windows;
using System.Windows.Controls;
using CircleToSearch.Ui;
using CircleToSearch.TextRecognition;
using System.Globalization;

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
        UiStrings strings,
        IReadOnlyList<OcrLanguageOption>? ocrLanguages = null)
    {
        var searchMode = CreateText(strings.SearchProvider(providerDisplayName));
        var runtime = CreateText(strings.GoogleLensRuntime(webView2Version));
        var gestureLabel = CreateText(strings.SettingsHotkeyLabel);
        var gesture = CreateInput(settings.HotkeyGesture);

        var maxSideLabel = CreateText(strings.SettingsMaxImageSideLabel);
        var maxSide = CreateInput(settings.MaxLongSidePx.ToString());

        var languages = ocrLanguages ?? [];
        var ocrLabel = CreateText(strings.SettingsOcrLanguageLabel);
        var ocrLanguage = CreateLanguagePicker(languages, strings.SystemDefaultLanguage, settings.OcrLanguageTag, includeDefault: true);
        var targetLabel = CreateText(strings.SettingsTranslationTargetLabel);
        var initialTarget = string.IsNullOrWhiteSpace(settings.TranslationTargetLanguageTag)
            ? DefaultTargetLanguageTag()
            : settings.TranslationTargetLanguageTag;
        var targetLanguage = CreateLanguagePicker(languages, strings.SystemDefaultLanguage, initialTarget, includeDefault: false);

        var status = CreateText(string.Empty);

        var apply = new Button { Content = strings.SettingsApply };
        apply.Click += (_, _) =>
        {
            try
            {
                settings.HotkeyGesture = gesture.Text.Trim();
                if (int.TryParse(maxSide.Text, out var max) && max is >= 256 and <= 8000)
                    settings.MaxLongSidePx = max;
                settings.OcrLanguageTag = ocrLanguage.SelectedValue as string ?? string.Empty;
                settings.TranslationTargetLanguageTag = targetLanguage.SelectedValue as string ??
                    DefaultTargetLanguageTag();
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
                ocrLabel,
                ocrLanguage,
                targetLabel,
                targetLanguage,
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

    private static ComboBox CreateLanguagePicker(
        IReadOnlyList<OcrLanguageOption> languages,
        string defaultName,
        string selectedTag,
        bool includeDefault)
    {
        var options = new List<OcrLanguageOption>();
        if (includeDefault) options.Add(new OcrLanguageOption(string.Empty, defaultName));
        options.AddRange(languages);
        if (!string.IsNullOrWhiteSpace(selectedTag) &&
            options.All(option => !string.Equals(option.Tag, selectedTag, StringComparison.OrdinalIgnoreCase)))
            options.Add(new OcrLanguageOption(selectedTag, selectedTag));
        var picker = new ComboBox
        {
            ItemsSource = options,
            DisplayMemberPath = nameof(OcrLanguageOption.DisplayName),
            SelectedValuePath = nameof(OcrLanguageOption.Tag),
            SelectedValue = selectedTag,
        };
        if (picker.SelectedIndex < 0 && options.Count > 0) picker.SelectedIndex = 0;
        picker.SetResourceReference(MarginProperty, ItemMarginResource);
        return picker;
    }

    private static string DefaultTargetLanguageTag() =>
        string.IsNullOrWhiteSpace(CultureInfo.CurrentUICulture.Name)
            ? "en"
            : CultureInfo.CurrentUICulture.Name;
}
