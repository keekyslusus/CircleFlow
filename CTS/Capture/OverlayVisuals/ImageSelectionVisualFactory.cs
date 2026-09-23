using CircleToSearch.Ui;

namespace CircleToSearch.Capture;

internal static class ImageSelectionVisualFactory
{
    internal static ImageSelectionVisual Create(bool lightTheme, UiStrings strings)
    {
        var toolbar = new FloatingToolbar(PluginPalette.For(lightTheme).FloatingToolbar);
        return new ImageSelectionVisual(toolbar, toolbar.AddAction(strings.TextSearch),
            toolbar.AddAction(strings.TextCopy), toolbar.AddAction(strings.ImageSave),
            toolbar.AddAction(strings.Translate), toolbar.AddAction(strings.Ask),
            toolbar.AddPrompt(strings.AskPlaceholder, strings.AskSend));
    }
}
