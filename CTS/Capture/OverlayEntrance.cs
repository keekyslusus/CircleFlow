namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

// Circle-to-search entrance: a translucent accent ripple washes out from the trigger
// point while particles twinkle along its wavefront. Plays once per selection; the
// layer removes itself when the longest timeline ends.
public static class OverlayEntrance
{
    private static readonly TimeSpan WashExpand = TimeSpan.FromMilliseconds(550);
    private static readonly TimeSpan WashHold = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan WashFade = TimeSpan.FromMilliseconds(280);
    private const double WashAlpha = 0.15;
    private const double ParticleDensity = 0.00012;
    private const int MaxParticles = 200;

    public static void Begin(Grid root, Point origin, Size size, Color accent)
    {
        if (!OverlayVisualFactory.AnimationsEnabled()) return;

        var canvas = new Canvas { IsHitTestVisible = false };
        // Below the chip (last child), above every selection layer.
        root.Children.Insert(root.Children.Count - 1, canvas);

        var storyboard = new Storyboard();
        AddRippleWash(storyboard, canvas, origin, size, accent);
        AddParticles(storyboard, canvas, origin, size, accent);
        storyboard.Completed += (_, _) => root.Children.Remove(canvas);
        storyboard.Begin(canvas, true);
    }

    private static void AddRippleWash(
        Storyboard storyboard,
        Canvas canvas,
        Point origin,
        Size size,
        Color accent)
    {
        // Uniform tint plus a luminous ring: gradients and blurs on huge scaled surfaces
        // band into rings under the software renderer, while halo-style elements render smooth.
        var maxRadius = FarthestCornerDistance(origin, size);
        var tint = new Rectangle
        {
            Width = size.Width,
            Height = size.Height,
            Fill = Frozen(WithAlpha(accent, WashAlpha)),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(tint, 0);
        Canvas.SetTop(tint, 0);
        canvas.Children.Add(tint);

        AddFade(storyboard, tint, new DoubleAnimation(0, 1, WashExpand) { EasingFunction = EaseOut() });
        AddFade(storyboard, tint, new DoubleAnimation(1, 0, WashFade) { BeginTime = WashExpand + WashHold });

        var ring = new Ellipse
        {
            Width = 2,
            Height = 2,
            Stroke = Frozen(WithAlpha(accent, 0.5)),
            StrokeThickness = 4,
            Effect = new BlurEffect { Radius = 8 },
            IsHitTestVisible = false,
        };
        canvas.Children.Add(ring);

        AddRingAnimation(storyboard, ring, FrameworkElement.WidthProperty, 2 * maxRadius, null);
        AddRingAnimation(storyboard, ring, FrameworkElement.HeightProperty, 2 * maxRadius, null);
        AddRingAnimation(storyboard, ring, Canvas.LeftProperty, origin.X - maxRadius, origin.X - 1);
        AddRingAnimation(storyboard, ring, Canvas.TopProperty, origin.Y - maxRadius, origin.Y - 1);
        AddFade(storyboard, ring, new DoubleAnimation(1, 0, WashFade) { BeginTime = WashExpand + WashHold });
    }

    private static void AddFade(Storyboard storyboard, UIElement target, DoubleAnimation fade)
    {
        Storyboard.SetTarget(fade, target);
        Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
        storyboard.Children.Add(fade);
    }

    private static void AddRingAnimation(
        Storyboard storyboard,
        Ellipse ring,
        DependencyProperty property,
        double toValue,
        double? fromValue)
    {
        var expand = fromValue is null
            ? new DoubleAnimation(toValue, WashExpand) { EasingFunction = EaseOut() }
            : new DoubleAnimation(fromValue.Value, toValue, WashExpand) { EasingFunction = EaseOut() };
        Storyboard.SetTarget(expand, ring);
        Storyboard.SetTargetProperty(expand, new PropertyPath(property));
        storyboard.Children.Add(expand);
    }

    private static void AddParticles(
        Storyboard storyboard,
        Canvas canvas,
        Point origin,
        Size size,
        Color accent)
    {
        // Offscreen particles are skipped, not clamped: clamping piles them into a visible
        // row along the near screen edges once the wave radius exceeds those edges.
        var count = (int)Math.Clamp(size.Width * size.Height * ParticleDensity * 1.15, 80, MaxParticles);
        var maxRadius = FarthestCornerDistance(origin, size);
        var random = Random.Shared;

        for (var i = 0; i < count; i++)
        {
            var useAccent = random.NextDouble() < 0.55;
            var wave = random.NextDouble();
            var angle = random.NextDouble() * 2 * Math.PI;
            var radius = wave * maxRadius * (0.9 + random.NextDouble() * 0.15);
            var dotSize = 2 + random.NextDouble() * 1.6;
            var peak = (useAccent ? 0.3 : 0.38) + random.NextDouble() * 0.25;
            var pulse = TimeSpan.FromMilliseconds(320 + random.NextDouble() * 330);
            var begin = TimeSpan.FromMilliseconds(
                wave * WashExpand.TotalMilliseconds * (0.85 + random.NextDouble() * 0.3)
                + random.NextDouble() * 140);

            var x = origin.X + Math.Cos(angle) * radius;
            var y = origin.Y + Math.Sin(angle) * radius;
            if (x < -4 || y < -4 || x > size.Width + 4 || y > size.Height + 4) continue;

            var dot = new Ellipse
            {
                Width = dotSize,
                Height = dotSize,
                Opacity = 0,
                Fill = Frozen(useAccent
                    ? WithAlpha(accent, 0.9)
                    : Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
            };
            Canvas.SetLeft(dot, x - dotSize / 2);
            Canvas.SetTop(dot, y - dotSize / 2);
            canvas.Children.Add(dot);

            // Fast attack, slow release: the dot pops in with the wave, then melts away.
            var twinkle = new DoubleAnimationUsingKeyFrames { BeginTime = begin };
            twinkle.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            twinkle.KeyFrames.Add(new EasingDoubleKeyFrame(peak, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(pulse.TotalMilliseconds * 0.35)))
            {
                EasingFunction = EaseOut(),
            });
            twinkle.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(pulse))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            });
            Storyboard.SetTarget(twinkle, dot);
            Storyboard.SetTargetProperty(twinkle, new PropertyPath(UIElement.OpacityProperty));
            storyboard.Children.Add(twinkle);
        }
    }

    private static double FarthestCornerDistance(Point origin, Size size)
    {
        var x = Math.Max(origin.X, size.Width - origin.X);
        var y = Math.Max(origin.Y, size.Height - origin.Y);
        return Math.Sqrt(x * x + y * y);
    }

    private static Color WithAlpha(Color color, double alpha) =>
        Color.FromArgb((byte)Math.Round(255 * alpha), color.R, color.G, color.B);

    private static IEasingFunction EaseOut() => new CubicEase { EasingMode = EasingMode.EaseOut };

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
