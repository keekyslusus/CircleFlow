using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace CircleToSearch.Ui.Effects;

public sealed class SceneRippleHost : ISceneRippleSink, IDisposable
{
    private const int MaximumActiveRipples = 4;
    private readonly Canvas _canvas;
    private readonly Queue<FrameworkElement> _active = new();
    private bool _disposed;

    public SceneRippleHost(Canvas canvas)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _canvas.IsHitTestVisible = false;
    }

    internal int ActiveCount => _active.Count;

    public void Emit(SceneRippleRequest request)
    {
        if (_disposed || !Capture.OverlayVisualFactory.AnimationsEnabled()) return;
        while (_active.Count >= MaximumActiveRipples) Remove(_active.Dequeue());

        var radius = FarthestCornerDistance(
            request.Origin,
            new Size(Math.Max(0, _canvas.ActualWidth), Math.Max(0, _canvas.ActualHeight)));
        var profile = Profile(request.Preset, Math.Clamp(request.Intensity, 0, 1));
        var ellipse = new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            StrokeThickness = profile.Thickness,
            Stroke = Frozen(profile.Color),
            Fill = request.Preset == SceneRipplePreset.Entrance
                ? Frozen(PluginPalette.WithAlpha(profile.Color, 0.08))
                : null,
            Opacity = 0,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(0.02, 0.02),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(ellipse, request.Origin.X - radius);
        Canvas.SetTop(ellipse, request.Origin.Y - radius);
        _canvas.Children.Add(ellipse);
        _active.Enqueue(ellipse);

        var duration = profile.Duration;
        ellipse.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimationUsingKeyFrames
        {
            KeyFrames =
            {
                new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                new EasingDoubleKeyFrame(profile.Opacity, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70))),
                new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(duration)),
            },
        });
        var scale = new DoubleAnimation(0.02, 1, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        scale.Completed += (_, _) =>
        {
            Remove(ellipse);
            if (_active.Count > 0 && ReferenceEquals(_active.Peek(), ellipse)) _active.Dequeue();
            else RemoveFromQueue(ellipse);
        };
        ((ScaleTransform)ellipse.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        ((ScaleTransform)ellipse.RenderTransform).BeginAnimation(
            ScaleTransform.ScaleYProperty,
            scale.Clone());
    }

    public static double FarthestCornerDistance(Point origin, Size size)
    {
        var x = Math.Max(Math.Abs(origin.X), Math.Abs(size.Width - origin.X));
        var y = Math.Max(Math.Abs(origin.Y), Math.Abs(size.Height - origin.Y));
        return Math.Sqrt(x * x + y * y);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var ripple in _active) Remove(ripple);
        _active.Clear();
    }

    private static RippleProfile Profile(SceneRipplePreset preset, double intensity) => preset switch
    {
        SceneRipplePreset.Entrance => new(
            PluginPalette.SceneRippleEntrance, 2, 0.22, TimeSpan.FromMilliseconds(850)),
        SceneRipplePreset.MusicMatch => new(
            PluginPalette.SceneRippleMatch, 5, 0.38, TimeSpan.FromMilliseconds(720)),
        _ => new(
            PluginPalette.SceneRippleAudio,
            2 + intensity * 2,
            0.12 + intensity * 0.18,
            TimeSpan.FromMilliseconds(520)),
    };

    private void Remove(FrameworkElement element)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        _canvas.Children.Remove(element);
    }

    private void RemoveFromQueue(FrameworkElement element)
    {
        if (_active.Count == 0) return;
        var retained = _active.Where(item => !ReferenceEquals(item, element)).ToArray();
        _active.Clear();
        foreach (var item in retained) _active.Enqueue(item);
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private sealed record RippleProfile(Color Color, double Thickness, double Opacity, TimeSpan Duration);
}
