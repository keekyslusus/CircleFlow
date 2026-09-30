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
        var copy = toolbar.AddAction(strings.TextCopy, PluginIcons.CopyOutlined);
        return new TextSelectionVisual(
            new Canvas { IsHitTestVisible = false },
            toolbar,
            copy,
            search);
    }
}
