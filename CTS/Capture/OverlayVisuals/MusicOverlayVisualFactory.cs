namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using CircleToSearch.Ui;

internal static class MusicOverlayVisualFactory
{
    internal static MusicOverlayVisual Create(
        Size size,
        bool lightTheme,
        UiStrings strings)
    {
        var palette = PluginPalette.For(lightTheme);
        var icon = new Path
        {
            Data = PluginIcons.MusicFilled,
            Fill = OverlayVisualResources.Frozen(palette.MusicButton.Foreground),
            Width = 18,
            Height = 18,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        icon.RenderTransform = new ScaleTransform(1, 1);
        var loadingIndicator = new LoadingIndicatorVisual
        {
            Fill = OverlayVisualResources.Frozen(palette.MusicButton.Foreground),
            Opacity = 0,
            Visibility = Visibility.Collapsed,
            RenderTransform = new ScaleTransform(0.72, 0.72),
        };
        var glyph = new Grid { Width = 42, Height = 42 };
        glyph.Children.Add(icon);
        glyph.Children.Add(loadingIndicator);
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
            Background = OverlayVisualResources.Frozen(palette.MusicButton.Surface),
            Foreground = OverlayVisualResources.Frozen(palette.MusicButton.Foreground),
            BorderBrush = OverlayVisualResources.Frozen(palette.MusicButton.Border),
            BorderThickness = new Thickness(1),
            ToolTip = strings.MusicRecognitionAction,
            Focusable = true,
            Cursor = Cursors.Hand,
            Effect = OverlayVisualResources.DockShadow(
                palette.MusicButton.Surface.A == 0xF0 ? 8 : 6,
                palette.MusicButton.Surface.A == 0xF0 ? 0.3 : 0.35),
        };
        OverlayVisualResources.ApplyButtonTemplate(
            button, button.Height / 2, palette.MusicButton.Hover, palette.MusicButton.Foreground);
        AutomationProperties.SetName(button, strings.MusicRecognitionAction);

        var waveform = new AudioWaveformVisual(lightTheme);
        var resultHost = new Grid
        {
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = Math.Max(0, size.Width - 32),
        };
        Panel.SetZIndex(resultHost, 1);
        return new MusicOverlayVisual(
            button,
            icon,
            loadingIndicator,
            waveform,
            resultHost);
    }
}
