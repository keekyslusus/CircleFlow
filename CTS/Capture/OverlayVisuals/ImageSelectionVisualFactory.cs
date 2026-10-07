using System.Windows;
using CircleToSearch.Ui;

namespace CircleToSearch.Capture;

internal static class ImageSelectionVisualFactory
{
    internal static ImageSelectionVisual Create(bool lightTheme, UiStrings strings, Thickness toolbarSafeInsets = default)
    {
        var toolbar = new FloatingToolbar(PluginPalette.For(lightTheme).FloatingToolbar, safeInsets: toolbarSafeInsets);
        var search = toolbar.AddAction(strings.TextSearch);
        toolbar.AddDivider();
        var ask = toolbar.AddAction(strings.Ask, PluginIcons.SparkleOutlined);
        var copy = toolbar.AddAction(strings.TextCopy, PluginIcons.CopyOutlined);
        var save = toolbar.AddAction(strings.ImageSave, PluginIcons.DownloadOutlined);
        var translate = toolbar.AddAction(strings.Translate, PluginIcons.TranslateOutlined);
        OverlayShortcuts.Search.AttachHint(search, strings);
        OverlayShortcuts.Copy.AttachHint(copy, strings);
        OverlayShortcuts.Save.AttachHint(save, strings);
        OverlayShortcuts.Translate.AttachHint(translate, strings);
        return new ImageSelectionVisual(toolbar, search, copy, save, translate, ask,
            toolbar.AddPrompt(strings.AskPlaceholder, strings.AskSend, PluginIcons.SparkleOutlined));
    }
}
