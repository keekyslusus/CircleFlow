namespace CircleToSearch.Capture;

using System.Windows.Controls;
using CircleToSearch.Ui;

internal static class TextSelectionVisualFactory
{
    internal static TextSelectionVisual Create(bool lightTheme, UiStrings strings)
    {
        var toolbar = new FloatingToolbar(PluginPalette.For(lightTheme).FloatingToolbar);
        var copy = toolbar.AddAction(strings.TextCopy);
        var search = toolbar.AddAction(strings.TextSearch);
        return new TextSelectionVisual(
            new Canvas { IsHitTestVisible = false },
            toolbar,
            copy,
            search);
    }
}
