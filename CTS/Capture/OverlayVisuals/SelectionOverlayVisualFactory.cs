namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CircleToSearch.Ui;

internal static class SelectionOverlayVisualFactory
{
    private const double HaloThickness = 12;
    private const double HaloBlurRadius = 8;
    private const double AccentThickness = 2.5;
    private const double DimBlurRadius = 28;
    private const double SheenBlurRadius = 14;
    private const double FrameGlowRadius = 18;
    // The dim is one translucent color with soft edges, which half resolution renders the same at a quarter of the
    // cost; at full resolution its full-screen blur, redrawn on every lasso or zoom frame, misses frames at 4K.
    private const double DimCacheScale = 0.5;
    internal const double RevealBleed = 96;
    internal const double FrameCornerRadius = 6;

    internal static SelectionOverlayVisual Create(BitmapSource? frame, Size size, Color accentColor)
    {
        var screenshot = new Image { Source = frame, Stretch = Stretch.Fill, IsHitTestVisible = false };
        var dim = new Path
        {
            Fill = OverlayVisualResources.Frozen(PluginPalette.SelectionDim),
            Data = SelectionOverlayTransitions.BuildRevealGeometry(size, []),
            IsHitTestVisible = false,
        };
        if (OverlayVisualResources.HardwareEffectsEnabled())
            dim.Effect = new BlurEffect { Radius = DimBlurRadius };

        var dimRect = new Path
        {
            Fill = OverlayVisualResources.Frozen(PluginPalette.SelectionDim),
            Data = Geometry.Empty,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        if (OverlayVisualResources.HardwareEffectsEnabled())
            dimRect.Effect = new BlurEffect { Radius = DimBlurRadius };

        var sheen = new Path
        {
            Fill = OverlayVisualResources.Frozen(PluginPalette.SelectionSheen),
            Data = Geometry.Empty,
            IsHitTestVisible = false,
        };
        if (OverlayVisualResources.HardwareEffectsEnabled())
            sheen.Effect = new BlurEffect { Radius = SheenBlurRadius };

        var halo = new Polyline
        {
            Stroke = OverlayVisualResources.Frozen(PluginPalette.SelectionHalo),
            StrokeThickness = HaloThickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        };
        if (OverlayVisualResources.HardwareEffectsEnabled())
            halo.Effect = new BlurEffect { Radius = HaloBlurRadius };

        var accent = new Polyline
        {
            Stroke = OverlayVisualResources.Frozen(accentColor),
            StrokeThickness = AccentThickness,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false,
        };
        var selectionFrame = new Path
        {
            Fill = OverlayVisualResources.Frozen(PluginPalette.SelectionFrameFill),
            Stroke = OverlayVisualResources.Frozen(accentColor),
            StrokeThickness = AccentThickness,
            Opacity = 0,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect
            {
                Color = accentColor,
                BlurRadius = FrameGlowRadius,
                ShadowDepth = 0,
                Opacity = 0.7,
            },
        };
        var inputSurface = new Grid
        {
            Background = OverlayVisualResources.Frozen(PluginPalette.Transparent),
            IsHitTestVisible = true,
        };
        // The cache holds everything inside it, so without this inner clip a zoomed dim, up to eight times the
        // screen, would grow the cache along with it.
        var dimClip = new Grid { IsHitTestVisible = false, ClipToBounds = true };
        dimClip.Children.Add(dim);
        dimClip.Children.Add(dimRect);
        var dimLayer = new Grid { IsHitTestVisible = false };
        dimLayer.Children.Add(dimClip);
        if (dim.Effect is not null) dimLayer.CacheMode = new BitmapCache { RenderAtScale = DimCacheScale };
        return new SelectionOverlayVisual(
            screenshot, dim, dimRect, sheen, halo, accent, selectionFrame, inputSurface, dimLayer);
    }
}
