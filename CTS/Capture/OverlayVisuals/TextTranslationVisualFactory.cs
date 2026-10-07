namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CircleToSearch.Ui;

internal static class TextTranslationVisualFactory
{
    internal static TranslationActionVisual CreateTranslationAction(bool lightTheme, UiStrings strings)
    {
        var palette = PluginPalette.For(lightTheme).Translation;
        var icon = new Path
        {
            Data = PluginIcons.TranslateFilled,
            Fill = OverlayVisualResources.Frozen(palette.Text),
            Width = 18,
            Height = 18,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        icon.RenderTransform = new ScaleTransform(1, 1);
        var loading = new LoadingIndicatorVisual
        {
            Fill = OverlayVisualResources.Frozen(palette.Text),
            Opacity = 0,
            Visibility = Visibility.Collapsed,
            RenderTransform = new ScaleTransform(0.72, 0.72),
        };
        var glyph = new Grid { Width = 42, Height = 42 };
        glyph.Children.Add(icon);
        glyph.Children.Add(loading);
        var button = new Button
        {
            Content = glyph,
            Width = 44,
            Height = 44,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Background = OverlayVisualResources.Frozen(palette.Surface),
            Foreground = OverlayVisualResources.Frozen(palette.Text),
            BorderBrush = OverlayVisualResources.Frozen(palette.Border),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = strings.Translate,
            Focusable = true,
            Effect = OverlayVisualResources.DockShadow(
                palette.Surface.A == 0xF0 ? 8 : 6,
                palette.Surface.A == 0xF0 ? 0.3 : 0.35),
        };
        OverlayVisualResources.ApplyButtonTemplate(button, 22, palette.Hover, palette.Text);
        AutomationProperties.SetName(button, strings.Translate);
        OverlayShortcuts.ScreenTranslation.AttachHint(button, strings);
        return new TranslationActionVisual(button, icon, loading);
    }

    internal static TranslationOverlayVisual CreateTranslationOverlay() => new(new Grid
    {
        Visibility = Visibility.Collapsed,
        Opacity = 0,
        IsHitTestVisible = false,
        HorizontalAlignment = HorizontalAlignment.Center,
    });
}
