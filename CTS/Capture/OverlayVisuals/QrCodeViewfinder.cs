namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using CircleToSearch.Ui;

// Corner brackets around a found code: they snap in once, thicken while its chip is hovered and pulse after a copy.
// Drawn like the lasso, an accent line over a soft halo, so they read on light and dark screens alike.
internal sealed class QrCodeViewfinder
{
    internal const double Thickness = 3;
    internal const double EmphasizedThickness = 4.5;
    internal const double EntranceScale = 1.25;
    internal const double PulseScale = 1.1;
    private const double HaloExtraThickness = 5;
    private const double HaloBlurRadius = 4;
    // Clear of the code's own quiet zone, so the brackets frame it instead of touching its modules.
    private const double Gap = 6;
    private const double MinCorner = 12;
    private const double MaxCorner = 28;
    private const double CornerShare = 0.25;
    private const double MaxRadius = PluginShapes.Medium;
    private static readonly TimeSpan EntranceDuration = TimeSpan.FromMilliseconds(360);
    private static readonly TimeSpan FadeInDuration = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan FadeOutDuration = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan EmphasisDuration = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan PulseHalfDuration = TimeSpan.FromMilliseconds(110);
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Path _halo;
    private readonly Path _accent;

    internal QrCodeViewfinder(Rect codeBounds, Color accent)
    {
        Frame = Rect.Inflate(codeBounds, Gap, Gap);
        var corners = Corners(Frame.Size);
        _halo = Stroke(corners, PluginPalette.SelectionHalo, Thickness + HaloExtraThickness);
        if (OverlayVisualResources.HardwareEffectsEnabled())
            _halo.Effect = new BlurEffect { Radius = HaloBlurRadius };
        _accent = Stroke(corners, accent, Thickness);
        Element = new Canvas
        {
            Width = Frame.Width,
            Height = Frame.Height,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = _scale,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        Element.Children.Add(_halo);
        Element.Children.Add(_accent);
        Canvas.SetLeft(Element, Frame.Left);
        Canvas.SetTop(Element, Frame.Top);
    }

    internal Canvas Element { get; }
    internal Rect Frame { get; }

    internal void Show(bool animate)
    {
        if (!animate)
        {
            Settle(opacity: 1);
            return;
        }
        Element.BeginAnimation(UIElement.OpacityProperty,
            OverlayVisualResources.Animate(Element.Opacity, 1, FadeInDuration));
        AnimateScale(new DoubleAnimation(EntranceScale, 1, EntranceDuration)
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 },
        });
    }

    internal void Hide(bool animate)
    {
        if (!animate)
        {
            Settle(opacity: 0);
            return;
        }
        Element.BeginAnimation(UIElement.OpacityProperty,
            OverlayVisualResources.Animate(Element.Opacity, 0, FadeOutDuration));
    }

    internal void SetEmphasized(bool emphasized, bool animate)
    {
        var thickness = emphasized ? EmphasizedThickness : Thickness;
        SetThickness(_accent, thickness, animate);
        SetThickness(_halo, thickness + HaloExtraThickness, animate);
    }

    internal void Pulse(bool animate)
    {
        if (!animate) return;
        AnimateScale(new DoubleAnimation(1, PulseScale, PulseHalfDuration)
        {
            AutoReverse = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    private void AnimateScale(DoubleAnimation animation)
    {
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    private void Settle(double opacity)
    {
        Element.BeginAnimation(UIElement.OpacityProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        Element.Opacity = opacity;
        _scale.ScaleX = 1;
        _scale.ScaleY = 1;
    }

    private static void SetThickness(Path path, double thickness, bool animate)
    {
        if (!animate)
        {
            path.BeginAnimation(Shape.StrokeThicknessProperty, null);
            path.StrokeThickness = thickness;
            return;
        }
        path.BeginAnimation(Shape.StrokeThicknessProperty,
            OverlayVisualResources.Animate(path.StrokeThickness, thickness, EmphasisDuration));
    }

    private static Path Stroke(Geometry corners, Color color, double thickness) => new()
    {
        Data = corners,
        Stroke = OverlayVisualResources.Frozen(color),
        StrokeThickness = thickness,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
    };

    private static Geometry Corners(Size size)
    {
        var corner = Math.Clamp(Math.Min(size.Width, size.Height) * CornerShare, MinCorner, MaxCorner);
        var radius = Math.Min(MaxRadius, corner / 2);
        var (w, h) = (size.Width, size.Height);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            Bracket(context, new(0, corner), new(0, radius), new(radius, 0), new(corner, 0), radius);
            Bracket(context, new(w - corner, 0), new(w - radius, 0), new(w, radius), new(w, corner), radius);
            Bracket(context, new(w, h - corner), new(w, h - radius), new(w - radius, h), new(w - corner, h), radius);
            Bracket(context, new(corner, h), new(radius, h), new(0, h - radius), new(0, h - corner), radius);
        }
        geometry.Freeze();
        return geometry;
    }

    private static void Bracket(StreamGeometryContext context, Point start, Point arcStart, Point arcEnd, Point end,
        double radius)
    {
        context.BeginFigure(start, isFilled: false, isClosed: false);
        context.LineTo(arcStart, isStroked: true, isSmoothJoin: true);
        context.ArcTo(arcEnd, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, true);
        context.LineTo(end, isStroked: true, isSmoothJoin: true);
    }
}
