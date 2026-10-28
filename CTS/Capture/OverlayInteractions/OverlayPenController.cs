using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CircleToSearch.Ui;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Capture.OverlayInteractions;

// Ctrl+drag draws on the frozen screen. The ink belongs to the picture handed to Ask, Copy and Save, while a search
// keeps reading the clean screen, since marks would only confuse image matching.
internal sealed class OverlayPenController : IDisposable
{
    private const double InkThickness = 4;
    private const double OutlineThickness = 7;
    private const double SampleDistanceDips = 1.5;
    private const string UndoToastKey = "ink-undo";
    private static readonly TimeSpan UndoFadeDuration = TimeSpan.FromMilliseconds(180);

    private readonly SelectionOverlayVisual _visual;
    private readonly FrameworkElement _cursorRoot;
    private readonly OverlayCoordinateMapper _mapper;
    private readonly Func<BitmapSource> _visibleImage;
    private readonly Func<bool> _canDraw;
    private readonly UiStrings _strings;
    private readonly Action<string, ToastNotification> _showToast;
    private readonly Func<bool> _animationsEnabled;
    private readonly Action<double>? _inked;
    private readonly Func<double> _zoom;
    private readonly List<Stroke> _strokes = [];
    private Stroke? _active;
    private bool _disposed;

    internal OverlayPenController(
        SelectionOverlayVisual visual,
        FrameworkElement cursorRoot,
        OverlayCoordinateMapper mapper,
        Func<BitmapSource> visibleImage,
        Func<bool> canDraw,
        UiStrings strings,
        Action<string, ToastNotification> showToast,
        Func<bool> animationsEnabled,
        Action<double>? inked = null,
        Func<double>? zoom = null)
    {
        _visual = visual;
        _cursorRoot = cursorRoot;
        _mapper = mapper;
        _visibleImage = visibleImage;
        _canDraw = canDraw;
        _strings = strings;
        _showToast = showToast;
        _animationsEnabled = animationsEnabled;
        _inked = inked;
        _zoom = zoom ?? (() => 1);
    }

    internal bool HasInk => _strokes.Count > 0;

    internal int StrokeCount => _strokes.Count;

    internal bool CanDraw => !_disposed && _canDraw();

    // The pen appears on a move with Ctrl held rather than on the key itself, so Ctrl shortcuts never flash it.
    internal bool Hover(bool penHeld)
    {
        if (penHeld && CanDraw)
        {
            _cursorRoot.Cursor = Cursors.Pen;
            return true;
        }
        // Back to the crosshair, which text hover then refines; a mode change may already have replaced the pen.
        if (ReferenceEquals(_cursorRoot.Cursor, Cursors.Pen)) _cursorRoot.Cursor = Cursors.Cross;
        return false;
    }

    internal bool Begin(Point point)
    {
        if (!CanDraw || _active is not null) return false;
        // Thickness is fixed in scene units when the stroke starts, so ink drawn while zoomed in stays as fine on
        // screen as unzoomed ink, and every later zoom scales it with the picture under it.
        var zoom = Math.Max(1, _zoom());
        _active = new Stroke(
            CreateLine(PluginPalette.PenInkOutline, OutlineThickness / zoom),
            CreateLine(PluginPalette.PenInk, InkThickness / zoom));
        _active.Add(point);
        _visual.Ink.Children.Insert(_strokes.Count, _active.Outline);
        _visual.Ink.Children.Add(_active.Ink);
        _cursorRoot.Cursor = Cursors.Pen;
        _visual.InputSurface.CaptureMouse();
        return true;
    }

    internal void Update(Point point)
    {
        if (_disposed || _active is null) return;
        var onScreen = (point - _active.Last).Length * Math.Max(1, _zoom());
        if (onScreen < SampleDistanceDips) return;
        _active.Add(point);
        _inked?.Invoke(onScreen);
    }

    internal void Complete(Point point)
    {
        if (_disposed || _active is null) return;
        var stroke = _active;
        _active = null;
        ReleaseMouseCapture();
        if (point != stroke.Last) stroke.Add(point);
        // A click without movement still leaves a dot, as a real pen would; a zero-length line draws no caps.
        if (stroke.Ink.Points.Count == 1) stroke.Add(point + new Vector(0.01, 0));
        _strokes.Add(stroke);
    }

    internal void Cancel()
    {
        if (_active is null) return;
        Remove(_active);
        _active = null;
        ReleaseMouseCapture();
    }

    internal bool TryHandleShortcut(Key key, ModifierKeys modifiers)
    {
        if (!OverlayShortcuts.UndoInk.Matches(key, modifiers) || _active is not null || !HasInk || !CanDraw)
            return false;
        var stroke = _strokes[^1];
        _strokes.RemoveAt(_strokes.Count - 1);
        FadeOut(stroke);
        // Repeated undos refresh one toast instead of stacking a toast per stroke.
        _showToast(UndoToastKey, new ToastNotification(_strings.InkUndone, ToastTone.Neutral));
        return true;
    }

    internal BitmapSource Crop(GdiRectangle bounds)
    {
        var source = _visibleImage();
        if (!HasInk) return VisibleImage.Crop(source, bounds);
        var scale = _mapper.Scale;
        var origin = _mapper.ToDips(new System.Drawing.Point(bounds.X, bounds.Y));
        var drawing = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(drawing, BitmapScalingMode.NearestNeighbor);
        using (var context = drawing.RenderOpen())
        {
            context.DrawImage(source, new Rect(-bounds.X, -bounds.Y, source.PixelWidth, source.PixelHeight));
            context.PushTransform(new MatrixTransform(scale, 0, 0, scale, -origin.X * scale, -origin.Y * scale));
            // All outlines go under all ink, as on screen, so crossing strokes do not cut through each other.
            foreach (var stroke in _strokes) Draw(context, stroke.Outline);
            foreach (var stroke in _strokes) Draw(context, stroke.Ink);
            context.Pop();
        }
        var output = new RenderTargetBitmap(bounds.Width, bounds.Height, 96, 96, PixelFormats.Pbgra32);
        output.Render(drawing);
        output.Freeze();
        return output;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Cancel();
    }

    // The stroke already left the picture, so only its look lingers while it fades.
    private void FadeOut(Stroke stroke)
    {
        if (!_animationsEnabled())
        {
            Remove(stroke);
            return;
        }
        var fade = new DoubleAnimation(0, UndoFadeDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        fade.Completed += (_, _) => Remove(stroke);
        stroke.Outline.BeginAnimation(UIElement.OpacityProperty, fade);
        stroke.Ink.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private void Remove(Stroke stroke)
    {
        _visual.Ink.Children.Remove(stroke.Outline);
        _visual.Ink.Children.Remove(stroke.Ink);
    }

    private void ReleaseMouseCapture()
    {
        if (ReferenceEquals(Mouse.Captured, _visual.InputSurface)) Mouse.Capture(null);
    }

    private static void Draw(DrawingContext context, Polyline line)
    {
        var geometry = new StreamGeometry();
        using (var figure = geometry.Open())
        {
            figure.BeginFigure(line.Points[0], isFilled: false, isClosed: false);
            figure.PolyLineTo(line.Points.Skip(1).ToList(), isStroked: true, isSmoothJoin: true);
        }
        geometry.Freeze();
        context.DrawGeometry(null, new Pen(line.Stroke, line.StrokeThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        }, geometry);
    }

    private static Polyline CreateLine(Color color, double thickness) => new()
    {
        Stroke = OverlayVisualResources.Frozen(color),
        StrokeThickness = thickness,
        StrokeLineJoin = PenLineJoin.Round,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        IsHitTestVisible = false,
    };

    private sealed record Stroke(Polyline Outline, Polyline Ink)
    {
        internal Point Last => Ink.Points[^1];

        internal void Add(Point point)
        {
            Outline.Points.Add(point);
            Ink.Points.Add(point);
        }
    }
}
