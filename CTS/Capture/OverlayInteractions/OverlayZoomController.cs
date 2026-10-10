using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using CircleToSearch.Ui;

namespace CircleToSearch.Capture.OverlayInteractions;

// Magnifies the frozen screen with Ctrl+wheel. Only the layers drawn on the screen itself move; their strokes and
// blurs are scaled back so the lasso keeps its look, and selection coordinates stay in screen space.
internal sealed class OverlayZoomController : IDisposable
{
    private const double FlingSampleMilliseconds = 80;
    private const double FlingIdleMilliseconds = 50;
    private const double MaxFrameSeconds = 0.05;

    private readonly FrameworkElement _viewport;
    private readonly UIElement _input;
    private readonly FrameworkElement _screenshot;
    private readonly SceneLayer[] _layers;
    private readonly Func<bool> _canZoom;
    private readonly Panel _backdrop;
    private readonly Brush? _backdropBackground;
    private readonly Action<double>? _zoomMoved;
    private readonly Action? _limitReached;
    private readonly OverlayZoomCamera _camera = new();
    private readonly Queue<(Point Position, int Timestamp)> _dragSamples = new();
    private TimeSpan? _lastFrame;
    private bool _animating;
    private bool _dragging;
    private bool _zoomed;
    private bool _uncovered;
    private bool _disposed;

    internal OverlayZoomController(
        FrameworkElement viewport,
        SelectionOverlayVisual selection,
        UIElement textHighlights,
        Panel backdrop,
        Func<bool> canZoom,
        Action<double>? zoomMoved = null,
        Action? limitReached = null)
    {
        _zoomMoved = zoomMoved;
        _limitReached = limitReached;
        _viewport = viewport;
        _input = selection.InputSurface;
        _screenshot = selection.Screenshot;
        _backdrop = backdrop;
        _backdropBackground = backdrop.Background;
        _canZoom = canZoom;
        _layers =
        [
            new SceneLayer(selection.Screenshot),
            new SceneLayer(selection.Dim),
            new SceneLayer(selection.DimRect),
            new SceneLayer(selection.Sheen),
            new SceneLayer(selection.Halo),
            new SceneLayer(selection.Accent),
            new SceneLayer(selection.SelectionFrame),
            new SceneLayer(textHighlights),
            new SceneLayer(selection.InputSurface),
        ];
        _input.MouseWheel += OnMouseWheel;
        _input.MouseDown += OnMouseDown;
        _input.MouseMove += OnMouseMove;
        _input.MouseUp += OnMouseUp;
        _input.LostMouseCapture += OnLostMouseCapture;
        _viewport.SizeChanged += OnViewportSizeChanged;
    }

    internal event Action? ViewChanged;

    internal event Action<bool>? ZoomedChanged;

    internal double Scale => _camera.Scale;

    internal Point ToViewport(Point scene) => _camera.ToViewport(scene);

    internal Rect ToViewport(Rect scene) => _camera.ToViewport(scene);

    internal bool TryHandleShortcut(Key key, ModifierKeys modifiers)
    {
        if (_disposed || modifiers != ModifierKeys.Control || !_canZoom()) return false;
        switch (key)
        {
            case Key.OemPlus or Key.Add:
                ZoomBy(1, KeyboardPivot());
                return true;
            case Key.OemMinus or Key.Subtract:
                ZoomBy(-1, KeyboardPivot());
                return true;
            case Key.D0 or Key.NumPad0:
                return TryReset();
            default:
                return false;
        }
    }

    // Glides back to the unzoomed screen; false when it is not zoomed or already returning.
    internal bool TryReset()
    {
        if (_disposed || !_canZoom() || !_camera.Reset()) return false;
        StartAnimation();
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        EndDrag();
        StopAnimation();
        _input.MouseWheel -= OnMouseWheel;
        _input.MouseDown -= OnMouseDown;
        _input.MouseMove -= OnMouseMove;
        _input.MouseUp -= OnMouseUp;
        _input.LostMouseCapture -= OnLostMouseCapture;
        _viewport.SizeChanged -= OnViewportSizeChanged;
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_disposed || e.Delta == 0 || !_canZoom()) return;
        var modifiers = Keyboard.Modifiers;
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            ZoomBy(e.Delta / 120.0, e.GetPosition(_viewport));
            e.Handled = true;
            return;
        }
        if (!_camera.IsZoomed || _dragging) return;
        // Same step as smooth scrolling elsewhere in the app; Shift turns the wheel sideways as in browsers.
        var step = SystemParameters.WheelScrollLines < 0
            ? _viewport.ActualHeight / 2
            : SystemParameters.WheelScrollLines * 32.0;
        var distance = e.Delta / 120.0 * step;
        _camera.ScrollBy(modifiers.HasFlag(ModifierKeys.Shift) ? new Vector(distance, 0) : new Vector(0, distance));
        StartAnimation();
        e.Handled = true;
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_disposed || e.ChangedButton != MouseButton.Middle) return;
        e.Handled = true;
        if (_dragging || !_camera.IsZoomed || !_canZoom() || Mouse.Captured is not null) return;
        _dragging = true;
        _camera.StopPan();
        _dragSamples.Clear();
        _dragSamples.Enqueue((e.GetPosition(_viewport), e.Timestamp));
        Mouse.OverrideCursor = Cursors.SizeAll;
        _input.CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var position = e.GetPosition(_viewport);
        var last = _dragSamples.Last().Position;
        _dragSamples.Enqueue((position, e.Timestamp));
        while (_dragSamples.Count > 2 && e.Timestamp - _dragSamples.Peek().Timestamp > FlingSampleMilliseconds)
            _dragSamples.Dequeue();
        _camera.PanBy(position - last);
        ApplyView();
        e.Handled = true;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging || e.ChangedButton != MouseButton.Middle) return;
        e.Handled = true;
        var first = _dragSamples.Peek();
        var last = _dragSamples.Last();
        var elapsed = last.Timestamp - first.Timestamp;
        EndDrag();
        if (elapsed <= 0 || e.Timestamp - last.Timestamp > FlingIdleMilliseconds) return;
        _camera.Fling((last.Position - first.Position) * (1000.0 / elapsed));
        StartAnimation();
    }

    private void OnLostMouseCapture(object sender, MouseEventArgs e) => EndDrag();

    private void EndDrag()
    {
        if (!_dragging) return;
        _dragging = false;
        _dragSamples.Clear();
        Mouse.OverrideCursor = null;
        if (ReferenceEquals(Mouse.Captured, _input)) Mouse.Capture(null);
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _camera.SetScreen(ScreenBounds());
        ApplyView();
    }

    // The screenshot sits inside the overlay's overscan edge, so it, not the window, bounds what may be uncovered.
    private Rect ScreenBounds() => _screenshot.RenderSize is { Width: > 0, Height: > 0 } size
        ? new Rect((Point)VisualTreeHelper.GetOffset(_screenshot), size)
        : new Rect(_viewport.RenderSize);

    private void ZoomBy(double notches, Point pivot)
    {
        _camera.SetScreen(ScreenBounds());
        if (_camera.ZoomBy(notches, pivot)) _limitReached?.Invoke();
        StartAnimation();
    }

    private Point KeyboardPivot()
    {
        var pointer = Mouse.GetPosition(_viewport);
        return pointer.X >= 0 && pointer.Y >= 0 && pointer.X <= _viewport.ActualWidth && pointer.Y <= _viewport.ActualHeight
            ? pointer
            : new Point(_viewport.ActualWidth / 2, _viewport.ActualHeight / 2);
    }

    private void StartAnimation()
    {
        if (_animating || _disposed) return;
        _animating = true;
        _lastFrame = null;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopAnimation()
    {
        if (!_animating) return;
        _animating = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // Frame time comes from the WPF animation clock, keeping this motion in step with the other animations.
        var now = ((RenderingEventArgs)e).RenderingTime;
        var elapsed = _lastFrame is { } last ? Math.Min((now - last).TotalSeconds, MaxFrameSeconds) : 0;
        _lastFrame = now;
        if (elapsed <= 0) return;
        var zoomed = _camera.Advance(elapsed);
        if (zoomed != 0) _zoomMoved?.Invoke(zoomed);
        ApplyView();
        if (!_camera.IsMoving) StopAnimation();
    }

    private void ApplyView()
    {
        if (_disposed) return;
        foreach (var layer in _layers) layer.Apply(_camera.Scale, _camera.Offset);
        // Only a bounce below the unzoomed screen uncovers the overlay; elsewhere the backdrop stays see-through,
        // since a widget result fades the screen out to show the desktop behind it.
        var uncovered = !_camera.CoversScreen;
        if (_uncovered != uncovered)
        {
            _uncovered = uncovered;
            _backdrop.Background = uncovered
                ? OverlayVisualResources.Frozen(PluginPalette.OpaqueBlack)
                : _backdropBackground;
        }
        if (_zoomed != _camera.IsZoomed)
        {
            _zoomed = _camera.IsZoomed;
            ZoomedChanged?.Invoke(_zoomed);
        }
        ViewChanged?.Invoke();
        // The screen moved under a still pointer, so the lasso and text hover follow it without waiting for a move.
        if (!_dragging) Mouse.Synchronize();
    }

    private sealed class SceneLayer
    {
        private readonly UIElement _element;
        private readonly MatrixTransform _transform = new();
        private readonly double _strokeThickness;
        private readonly double _effectRadius;

        internal SceneLayer(UIElement element)
        {
            _element = element;
            _element.RenderTransform = _transform;
            _strokeThickness = element is Shape shape ? shape.StrokeThickness : 0;
            _effectRadius = EffectRadius(element.Effect);
        }

        internal void Apply(double scale, Vector offset)
        {
            // A layer placed with a margin is offset from the viewport origin, which its own transform must undo.
            var layout = VisualTreeHelper.GetOffset(_element);
            _transform.Matrix = new Matrix(scale, 0, 0, scale,
                offset.X + (scale - 1) * layout.X,
                offset.Y + (scale - 1) * layout.Y);
            if (_element is Shape shape && _strokeThickness > 0) shape.StrokeThickness = _strokeThickness / scale;
            switch (_element.Effect)
            {
                case BlurEffect { IsFrozen: false } blur when _effectRadius > 0:
                    blur.Radius = _effectRadius / scale;
                    break;
                case DropShadowEffect { IsFrozen: false } shadow when _effectRadius > 0:
                    shadow.BlurRadius = _effectRadius / scale;
                    break;
            }
        }

        private static double EffectRadius(Effect? effect) => effect switch
        {
            BlurEffect blur => blur.Radius,
            DropShadowEffect shadow => shadow.BlurRadius,
            _ => 0,
        };
    }
}
