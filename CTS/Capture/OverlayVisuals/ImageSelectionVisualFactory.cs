using CircleToSearch.Ui;

namespace CircleToSearch.Capture;

internal static class ImageSelectionVisualFactory
{
    internal static ImageSelectionVisual Create(bool lightTheme, UiStrings strings)
    {
        var toolbar = new FloatingToolbar(PluginPalette.For(lightTheme).FloatingToolbar);
        var search = toolbar.AddAction(strings.TextSearch);
        toolbar.AddDivider();
        var ask = toolbar.AddAction(strings.Ask, PluginIcons.SparkleOutlined);
        var copy = toolbar.AddAction(strings.TextCopy, PluginIcons.CopyOutlined);
        var save = toolbar.AddAction(strings.ImageSave, PluginIcons.DownloadOutlined);
        var translate = toolbar.AddAction(strings.Translate, PluginIcons.TranslateOutlined);
        return new ImageSelectionVisual(toolbar, search, copy, save, translate, ask,
            toolbar.AddPrompt(strings.AskPlaceholder, strings.AskSend));
    }
}
