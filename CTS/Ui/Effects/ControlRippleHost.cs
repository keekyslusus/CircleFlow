using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace CircleToSearch.Ui.Effects;

// An adorner keeps the effect clipped to the control without changing its template or hit testing.
public sealed class ControlRippleHost : IDisposable
{
    private static readonly TimeSpan DefaultExpandDuration = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan CompactExpandDuration = TimeSpan.FromMilliseconds(225);
    private readonly Control _control;
    private readonly bool _animationsEnabled;
    private RippleAdorner? _adorner;

    private ControlRippleHost(Control control, bool animationsEnabled)
    {
        _control = control;
        _animationsEnabled = animationsEnabled;
        control.PreviewMouseLeftButtonDown += OnPointerDown;
        control.PreviewMouseLeftButtonUp += OnPointerUp;
        control.Unloaded += OnUnloaded;
    }

    public static ControlRippleHost Attach(Control control) =>
        new(control, Capture.OverlayVisualResources.AnimationsEnabled());

    internal static ControlRippleHost AttachForTest(Control control) => new(control, true);

    private void OnPointerDown(object sender, MouseButtonEventArgs e)
    {
        if (!_animationsEnabled) return;
        var layer = AdornerLayer.GetAdornerLayer(_control);
        if (layer is null) return;
        RemoveActiveRipple();
        var duration = _control.Tag is string ? CompactExpandDuration : DefaultExpandDuration;
        var foreground = ResolveForegroundColor(_control);
        var adorner = new RippleAdorner(
            _control,
            e.GetPosition(_control),
            PluginPalette.WithAlpha(foreground, 0.21),
            duration,
            ControlCornerRadius(_control));
        _adorner = adorner;
        layer.Add(adorner);
        adorner.Begin(() =>
        {
            if (!ReferenceEquals(_adorner, adorner)) return;
            layer.Remove(adorner);
            _adorner = null;
        });
    }

    private void OnPointerUp(object sender, MouseButtonEventArgs e) => _adorner?.Release();

    private void OnUnloaded(object sender, RoutedEventArgs e) => Dispose();

    public void Dispose()
    {
        _control.PreviewMouseLeftButtonDown -= OnPointerDown;
        _control.PreviewMouseLeftButtonUp -= OnPointerUp;
        _control.Unloaded -= OnUnloaded;
        RemoveActiveRipple();
    }

    private void RemoveActiveRipple()
    {
        if (_adorner is null) return;
        var adorner = _adorner;
        _adorner = null;
        adorner.Cancel();
        AdornerLayer.GetAdornerLayer(_control)?.Remove(adorner);
    }

    private static Color ResolveForegroundColor(Control control) =>
        control.Foreground is SolidColorBrush brush
            ? brush.Color
            : PluginPalette.OpaqueBlack;

    private static double ControlCornerRadius(Control control)
    {
        control.ApplyTemplate();
        return control.Template.FindName("Chrome", control) is Border border
            ? border.CornerRadius.TopLeft
            : Math.Min(control.ActualWidth, control.ActualHeight) / 2;
    }

    private sealed class RippleAdorner : Adorner
    {
        private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(200);
        private static readonly TimeSpan WaitBeforeFade = TimeSpan.FromMilliseconds(290);
        private static readonly KeySpline ExpandSpline = new(0, 0.49, 0, 1);
        private static readonly KeySpline FadeSpline = new(0.11, 0, 0.5, 0);
        private readonly Canvas _layer;
        private readonly Ellipse _ellipse;
        private readonly GradientStop _solidStop;
        private readonly VisualCollection _visuals;
        private readonly TimeSpan _expandDuration;
        private readonly double _cornerRadius;
        private readonly Stopwatch _elapsed = new();
        private DispatcherTimer? _releaseTimer;
        private DispatcherTimer? _fallbackTimer;
        private Action? _completed;
        private bool _releaseRequested;
        private bool _finished;

        public RippleAdorner(
            UIElement adornedElement,
            Point origin,
            Color color,
            TimeSpan expandDuration,
            double cornerRadius) : base(adornedElement)
        {
            _expandDuration = expandDuration;
            _cornerRadius = cornerRadius;
            _visuals = new VisualCollection(this);
            IsHitTestVisible = false;
            var width = adornedElement.RenderSize.Width;
            var height = adornedElement.RenderSize.Height;
            var maxX = Math.Max(origin.X, width - origin.X);
            var maxY = Math.Max(origin.Y, height - origin.Y);
            var diameter = Math.Sqrt(maxX * maxX + maxY * maxY) * 2 / 0.8;
            var transparent = PluginPalette.WithAlpha(color, 0);
            _solidStop = new GradientStop(color, 0);
            var brush = new RadialGradientBrush
            {
                GradientStops =
                {
                    _solidStop,
                    new GradientStop(transparent, 1),
                },
            };
            _ellipse = new Ellipse
            {
                Width = diameter,
                Height = diameter,
                Fill = brush,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1d / 6, 1d / 6),
            };
            Canvas.SetLeft(_ellipse, origin.X - diameter / 2);
            Canvas.SetTop(_ellipse, origin.Y - diameter / 2);

            _layer = new Canvas { IsHitTestVisible = false };
            _layer.Children.Add(_ellipse);
            _visuals.Add(_layer);
        }

        public void Begin(Action completed)
        {
            _completed = completed;
            _elapsed.Start();
            var scale = (ScaleTransform)_ellipse.RenderTransform;
            scale.BeginAnimation(
                ScaleTransform.ScaleXProperty,
                SplineAnimation(1d / 6, 1, _expandDuration, ExpandSpline));
            scale.BeginAnimation(
                ScaleTransform.ScaleYProperty,
                SplineAnimation(1d / 6, 1, _expandDuration, ExpandSpline));
            _solidStop.BeginAnimation(
                GradientStop.OffsetProperty,
                SplineAnimation(0, 0.8, _expandDuration, ExpandSpline));
            _fallbackTimer = OneShot(
                _expandDuration + FadeDuration + TimeSpan.FromMilliseconds(80),
                Release);
        }

        public void Release()
        {
            if (_releaseRequested || _finished) return;
            _releaseRequested = true;
            var elapsed = _elapsed.Elapsed;
            var delay = elapsed < WaitBeforeFade
                ? WaitBeforeFade - elapsed
                : elapsed < _expandDuration
                    ? _expandDuration - elapsed
                    : TimeSpan.Zero;
            if (delay <= TimeSpan.Zero)
            {
                BeginFade();
                return;
            }
            _releaseTimer = OneShot(delay, BeginFade);
        }

        public void Cancel()
        {
            if (_finished) return;
            _finished = true;
            StopTimers();
            _completed = null;
        }

        private void BeginFade()
        {
            if (_finished) return;
            var fade = SplineAnimation(_ellipse.Opacity, 0, FadeDuration, FadeSpline);
            fade.Completed += (_, _) => Complete();
            _ellipse.BeginAnimation(OpacityProperty, fade);
        }

        private void Complete()
        {
            if (_finished) return;
            _finished = true;
            StopTimers();
            var completed = _completed;
            _completed = null;
            completed?.Invoke();
        }

        private void StopTimers()
        {
            _releaseTimer?.Stop();
            _releaseTimer = null;
            _fallbackTimer?.Stop();
            _fallbackTimer = null;
            _elapsed.Stop();
        }

        private static DoubleAnimationUsingKeyFrames SplineAnimation(
            double from,
            double to,
            TimeSpan duration,
            KeySpline spline)
        {
            var animation = new DoubleAnimationUsingKeyFrames { Duration = duration };
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(duration), spline));
            return animation;
        }

        private static DispatcherTimer OneShot(TimeSpan delay, Action action)
        {
            var timer = new DispatcherTimer { Interval = delay };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                action();
            };
            timer.Start();
            return timer;
        }

        protected override int VisualChildrenCount => _visuals.Count;
        protected override Visual GetVisualChild(int index) => _visuals[index];

        protected override Size ArrangeOverride(Size finalSize)
        {
            Clip = new RectangleGeometry(
                new Rect(finalSize),
                Math.Min(_cornerRadius, finalSize.Width / 2),
                Math.Min(_cornerRadius, finalSize.Height / 2));
            _layer.Arrange(new Rect(finalSize));
            return finalSize;
        }
    }
}
