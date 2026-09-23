namespace CircleToSearch.Capture;

using System.Windows.Controls;
using CircleToSearch.Ui;

internal static class TextSelectionVisualFactory
{
    internal static TextSelectionVisual Create(bool lightTheme, UiStrings strings)
    {
        var toolbar = new FloatingToolbar(PluginPalette.For(lightTheme).FloatingToolbar);
        var search = toolbar.AddAction(strings.TextSearch);
        var copy = toolbar.AddAction(strings.TextCopy);
        return new TextSelectionVisual(
            new Canvas { IsHitTestVisible = false },
            toolbar,
            copy,
            search);
    }
}
