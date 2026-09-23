using System.Windows;
using System.Windows.Media;
using CircleToSearch.Ui;

namespace CircleToSearch.Capture;

internal static class ImageSelectionVisualFactory
{
    private const double AskMarkSize = 18;

    internal static ImageSelectionVisual Create(bool lightTheme, UiStrings strings)
    {
        var toolbar = new FloatingToolbar(PluginPalette.For(lightTheme).FloatingToolbar);
        var search = toolbar.AddAction(strings.TextSearch);
        var ask = toolbar.AddAction(strings.Ask, CreateAskMark());
        var copy = toolbar.AddAction(strings.TextCopy);
        var save = toolbar.AddAction(strings.ImageSave);
        var translate = toolbar.AddAction(strings.Translate);
        return new ImageSelectionVisual(toolbar, search, copy, save, translate, ask,
            toolbar.AddPrompt(strings.AskPlaceholder, strings.AskSend));
    }

    // Radial glows stand in for the blurred color fields of the Gemini sparkle, which geometry drawings cannot blur.
    private static FrameworkElement CreateAskMark() => OverlayVisualResources.BrandMark(AskMarkSize,
        (OverlayVisualResources.Frozen(PluginPalette.GeminiBlue), PluginIcons.GeminiSparkle),
        (Glow(PluginPalette.GeminiRed, new Point(8.2, 3.2), 9), PluginIcons.GeminiSparkle),
        (Glow(PluginPalette.GeminiYellow, new Point(2.4, 12), 6.5), PluginIcons.GeminiSparkle),
        (Glow(PluginPalette.GeminiGreen, new Point(8.6, 21), 9), PluginIcons.GeminiSparkle));

    private static Brush Glow(Color color, Point center, double radius)
    {
        var brush = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = center,
            GradientOrigin = center,
            RadiusX = radius,
            RadiusY = radius,
        };
        brush.GradientStops.Add(new GradientStop(color, 0));
        brush.GradientStops.Add(new GradientStop(color, 0.35));
        brush.GradientStops.Add(new GradientStop(PluginPalette.WithAlpha(color, 0), 1));
        brush.Freeze();
        return brush;
    }
}
