namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using CircleToSearch.Ui;

internal static class MusicOverlayVisualFactory
{
    internal static readonly Geometry MusicIconGeometry = CreateMusicIconGeometry();

    internal static MusicOverlayVisual Create(
        Size size,
        bool lightTheme,
        UiStrings strings)
    {
        var palette = PluginPalette.For(lightTheme);
        var icon = new Path
        {
            Data = MusicIconGeometry,
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
            button, 22, palette.MusicButton.Hover, palette.MusicButton.Foreground);
        AutomationProperties.SetName(button, strings.MusicRecognitionAction);

        var waveform = new AudioWaveformVisual(lightTheme);
        var listeningLayer = new StackPanel
        {
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, size.Height * 0.45 - 34, 0, 0),
            Opacity = 0,
            IsHitTestVisible = false,
        };
        listeningLayer.Children.Add(waveform);
        listeningLayer.Children.Add(new TextBlock
        {
            Text = strings.Listening,
            Foreground = OverlayVisualResources.Frozen(PluginPalette.ListeningText),
            HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = 14,
            FontWeight = FontWeights.Medium,
            FontFamily = OverlayVisualResources.Font,
            Margin = new Thickness(0, 10, 0, 0),
            Effect = new DropShadowEffect
            {
                Color = PluginPalette.OpaqueBlack,
                BlurRadius = 10,
                ShadowDepth = 1,
                Opacity = 0.4,
            },
        });

        var resultHost = new Grid
        {
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Panel.SetZIndex(resultHost, 1);
        return new MusicOverlayVisual(
            button,
            icon,
            loadingIndicator,
            listeningLayer,
            waveform,
            resultHost);
    }

    private static Geometry CreateMusicIconGeometry()
    {
        var geometry = Geometry.Parse("M12 3v10.55A4 4 0 1 0 14 17V7h4V3h-6Z");
        geometry.Freeze();
        return geometry;
    }
}
