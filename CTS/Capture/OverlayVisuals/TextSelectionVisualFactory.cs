namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Controls;
using CircleToSearch.Ui;

internal static class TextSelectionVisualFactory
{
    internal static TextSelectionVisual Create(bool lightTheme, UiStrings strings, Thickness toolbarSafeInsets = default)
    {
        var toolbar = new FloatingToolbar(PluginPalette.For(lightTheme).FloatingToolbar, safeInsets: toolbarSafeInsets);
        var search = toolbar.AddAction(strings.TextSearch);
        // Shown only while the selection is a link; its label is the host it would open.
        var openLink = toolbar.AddAction(string.Empty, PluginIcons.LinkOutlined);
        openLink.Visibility = Visibility.Collapsed;
        var copy = toolbar.AddAction(strings.TextCopy, PluginIcons.CopyOutlined);
        OverlayShortcuts.Search.AttachHint(search, strings);
        OverlayShortcuts.Copy.AttachHint(copy, strings);
        return new TextSelectionVisual(
            new Canvas { IsHitTestVisible = false },
            toolbar,
            copy,
            search,
            openLink);
    }
}
