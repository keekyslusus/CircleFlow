using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Interop;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Capture.OverlayInteractions;

internal sealed class SelectionOverlayController : IDisposable
{
    private const double SampleDistanceDips = 3;
    private static readonly TimeSpan SelectionHoldDuration = TimeSpan.FromMilliseconds(450);

    private readonly SelectionOverlayVisual _visual;
    private readonly FrameworkElement _coordinateRoot;
    private readonly OverlayCoordinateMapper _coordinateMapper;
    private readonly int _paddingPx;
    private readonly int _minDiagonalPx;
    private readonly Func<bool> _canAcceptInput;
    private readonly Func<object?, Point, bool> _canStartSelection;
    private readonly Func<MouseEventArgs, Point> _pointerPosition;
    private readonly Action _selectionStarted;
    private readonly Action<GdiRectangle> _selectionCompleted;
    private readonly Action _selectionRejected;
    private readonly Action _holdCompleted;
    private readonly ImageActionCardVisual? _imageActions;
    private readonly Func<GdiRectangle, SelectionOutcome>? _createSelectionCopy;
    private readonly ClipboardCopyService? _clipboardCopy;
    private readonly Action? _requestCancel;
    private readonly LassoPathSampler _sampler;
    private readonly List<Point> _stroke = [];
    private DispatcherTimer? _holdTimer;
    private bool _drawing;
    private bool _isActionMenu;
    private GdiRectangle? _actionBounds;
    private bool _revealUpdateQueued;
    private bool _disposed;

    internal bool HasPendingRevealUpdate => _revealUpdateQueued;

    internal bool HasPendingHold => _holdTimer is not null;

    internal bool IsActionMenuOpen => _imageActions?.ActionCard.Visibility == Visibility.Visible;

    internal SelectionOverlayController(
        SelectionOverlayVisual visual,
        FrameworkElement coordinateRoot,
        GdiRectangle monitor,
        double scale,
        int paddingPx,
        int minDiagonalPx,
        bool overscan,
        Func<bool> canAcceptInput,
        Func<object?, Point, bool> canStartSelection,
        Action selectionStarted,
        Action<GdiRectangle> selectionCompleted,
        Action selectionRejected,
        Action holdCompleted,
        ImageActionCardVisual? imageActions = null,
        Func<GdiRectangle, SelectionOutcome>? createSelectionCopy = null,
        ClipboardCopyService? clipboardCopy = null,
        Action? requestCancel = null,
        Func<MouseEventArgs, Point>? pointerPosition = null,
        bool subscribeInput = true)
    {
        _visual = visual;
        _coordinateRoot = coordinateRoot;
        _coordinateMapper = new OverlayCoordinateMapper(scale, overscan, monitor.Size);
        _paddingPx = paddingPx;
        _minDiagonalPx = minDiagonalPx;
        _canAcceptInput = canAcceptInput;
        _canStartSelection = canStartSelection;
        _pointerPosition = pointerPosition ?? (e => e.GetPosition(_coordinateRoot));
        _selectionStarted = selectionStarted;
        _selectionCompleted = selectionCompleted;
        _selectionRejected = selectionRejected;
        _holdCompleted = holdCompleted;
        _imageActions = imageActions;
        _createSelectionCopy = createSelectionCopy;
        _clipboardCopy = clipboardCopy;
        _requestCancel = requestCancel;
        _sampler = new LassoPathSampler(SampleDistanceDips * scale);

        if (_imageActions is not null)
        {
            _imageActions.CopyButton.Click += OnActionCopy;
            _imageActions.SaveButton.Click += OnActionSave;
            _imageActions.SearchButton.Click += OnActionSearch;
        }

        if (subscribeInput)
        {
            _visual.InputSurface.MouseLeftButtonDown += OnMouseLeftButtonDown;
            _visual.InputSurface.MouseMove += OnMouseMove;
            _visual.InputSurface.MouseLeftButtonUp += OnMouseLeftButtonUp;
        }
    }

    internal bool Begin(Point point, object? originalSource = null, bool isActionMenu = false)
    {
        if (_disposed || !_canAcceptInput() || !_canStartSelection(originalSource, point)) return false;
        DismissActionMenu();
        _isActionMenu = isActionMenu;
        _drawing = true;
        _selectionStarted();
        _sampler.Reset();
        _stroke.Clear();
        Track(point);
        _visual.InputSurface.CaptureMouse();
        return true;
    }

    internal void Update(Point point)
    {
        if (_disposed || !_drawing || !_canAcceptInput()) return;
        Track(point);
    }

    internal void Complete(Point point)
    {
        if (_disposed || !_drawing || !_canAcceptInput()) return;
        _drawing = false;
        ReleaseMouseCapture();
        Track(point, final: true);
        CompleteGesture();
    }

    internal void Cancel() => StopInput();

    internal void StopInput()
    {
        _drawing = false;
        UnqueueRevealUpdate();
        ReleaseMouseCapture();
    }

    internal void FadeSelectionVisuals()
    {
        UIElement[] targets =
        [
            _visual.Screenshot,
            _visual.Sheen,
            _visual.Halo,
            _visual.Accent,
            _visual.SelectionFrame,
        ];
        if (!OverlayVisualResources.AnimationsEnabled())
        {
            foreach (var target in targets) target.Opacity = 0;
            return;
        }
        var duration = TimeSpan.FromMilliseconds(200);
        foreach (var target in targets)
            target.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(target.Opacity, 0, duration)
            {
                EasingFunction = EaseOut(),
            });
    }

    internal void RestoreSelectionVisuals()
    {
        ResetSelectionGesture();
        UIElement[] targets = [_visual.Screenshot, _visual.Sheen, _visual.Halo, _visual.Accent];
        if (!OverlayVisualResources.AnimationsEnabled())
        {
            foreach (var target in targets) target.Opacity = 1;
            return;
        }
        var duration = TimeSpan.FromMilliseconds(200);
        foreach (var target in targets)
            target.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(target.Opacity, 1, duration)
            {
                EasingFunction = EaseOut(),
            });
    }

    internal void ShowSelectionFrame(GdiRectangle bounds)
    {
        UnqueueRevealUpdate();
        var size = new Size(_coordinateRoot.ActualWidth, _coordinateRoot.ActualHeight);
        var rect = _coordinateMapper.ToDips(bounds);
        Point[] corners =
        [
            new(rect.Left, rect.Top),
            new(rect.Right, rect.Top),
            new(rect.Right, rect.Bottom),
            new(rect.Left, rect.Bottom),
        ];
        SelectionOverlayTransitions.BeginSelectionReveal(
            _visual,
            SelectionOverlayTransitions.BuildRevealGeometry(size, corners),
            SelectionOverlayTransitions.BuildSelectionFrameGeometry(rect));

        StopHoldTimer();
        _holdTimer = new DispatcherTimer { Interval = SelectionHoldDuration };
        _holdTimer.Tick += OnHoldCompleted;
        _holdTimer.Start();
    }

    internal void DismissActionMenu()
    {
        if (_imageActions is not null)
            _imageActions.ActionCard.Visibility = Visibility.Collapsed;
        _actionBounds = null;
        ResetSelectionGesture();
    }

    internal void ShowActionMenu(GdiRectangle bounds)
    {
        _actionBounds = bounds;
        UnqueueRevealUpdate();
        var size = new Size(_coordinateRoot.ActualWidth, _coordinateRoot.ActualHeight);
        var rect = _coordinateMapper.ToDips(bounds);
        Point[] corners =
        [
            new(rect.Left, rect.Top),
            new(rect.Right, rect.Top),
            new(rect.Right, rect.Bottom),
            new(rect.Left, rect.Bottom),
        ];
        SelectionOverlayTransitions.BeginSelectionReveal(
            _visual,
            SelectionOverlayTransitions.BuildRevealGeometry(size, corners),
            SelectionOverlayTransitions.BuildSelectionFrameGeometry(rect));

        if (_imageActions is null) return;
        _imageActions.ActionCard.Visibility = Visibility.Visible;
        _imageActions.ActionCard.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var desired = _imageActions.ActionCard.DesiredSize;
        var placement = TextActionCardLayout.Place(rect, desired, size);
        Canvas.SetLeft(_imageActions.ActionCard, placement.X);
        Canvas.SetTop(_imageActions.ActionCard, placement.Y);
    }

    internal void TriggerCopy()
    {
        if (_actionBounds is null || _createSelectionCopy is null || _clipboardCopy is null) return;
        using var outcome = _createSelectionCopy(_actionBounds.Value);
        var source = CreateFrozenFrame(outcome.FrozenFrame, outcome.Bounds);
        DismissActionMenu();
        if (_clipboardCopy.TryCopyImage(source))
        {
            _requestCancel?.Invoke();
        }
    }

    internal void TriggerSave()
    {
        if (_actionBounds is null || _createSelectionCopy is null) return;
        using var outcome = _createSelectionCopy(_actionBounds.Value);
        var frame = outcome.FrozenFrame;
        var cropRect = GdiRectangle.Intersect(outcome.Bounds, new GdiRectangle(0, 0, frame.Width, frame.Height));
        using var cropped = frame.Clone(cropRect, frame.PixelFormat);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg|Bitmap Image (*.bmp)|*.bmp",
            DefaultExt = ".png",
            FileName = $"CircleFlow_{DateTime.Now:yyyyMMdd_HHmmss}.png",
        };
        if (dialog.ShowDialog() == true)
        {
            var ext = System.IO.Path.GetExtension(dialog.FileName).ToLowerInvariant();
            var format = ext switch
            {
                ".jpg" or ".jpeg" => System.Drawing.Imaging.ImageFormat.Jpeg,
                ".bmp" => System.Drawing.Imaging.ImageFormat.Bmp,
                _ => System.Drawing.Imaging.ImageFormat.Png,
            };
            cropped.Save(dialog.FileName, format);
            DismissActionMenu();
            _requestCancel?.Invoke();
        }
    }

    internal void TriggerSearch()
    {
        if (_actionBounds is null) return;
        var bounds = _actionBounds.Value;
        if (_imageActions is not null)
            _imageActions.ActionCard.Visibility = Visibility.Collapsed;
        _selectionCompleted(bounds);
        if (!_disposed) ShowSelectionFrame(bounds);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _visual.InputSurface.MouseLeftButtonDown -= OnMouseLeftButtonDown;
        _visual.InputSurface.MouseMove -= OnMouseMove;
        _visual.InputSurface.MouseLeftButtonUp -= OnMouseLeftButtonUp;
        if (_imageActions is not null)
        {
            _imageActions.CopyButton.Click -= OnActionCopy;
            _imageActions.SaveButton.Click -= OnActionSave;
            _imageActions.SearchButton.Click -= OnActionSearch;
            _imageActions.ActionCard.Visibility = Visibility.Collapsed;
        }
        StopInput();
        StopHoldTimer();
    }

    private void OnActionCopy(object sender, RoutedEventArgs e)
    {
        TriggerCopy();
        e.Handled = true;
    }

    private void OnActionSave(object sender, RoutedEventArgs e)
    {
        TriggerSave();
        e.Handled = true;
    }

    private void OnActionSearch(object sender, RoutedEventArgs e)
    {
        TriggerSearch();
        e.Handled = true;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_disposed || !_canAcceptInput()) return;
        if (!Begin(_pointerPosition(e), e.OriginalSource))
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_disposed || !_drawing || !_canAcceptInput()) return;
        Update(_pointerPosition(e));
        e.Handled = true;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_disposed || !_drawing || !_canAcceptInput()) return;
        Complete(_pointerPosition(e));
        e.Handled = true;
    }

    private void CompleteGesture()
    {
        var bounds = LassoBoundsCalculator.Calculate(
            _sampler.Points,
            _coordinateMapper.CaptureBounds,
            _paddingPx,
            _minDiagonalPx);
        if (bounds is null)
        {
            ResetSelectionGesture();
            _selectionRejected();
            return;
        }
        if (_isActionMenu && _imageActions is not null)
        {
            ShowActionMenu(bounds.Value);
        }
        else
        {
            _selectionCompleted(bounds.Value);
            if (!_disposed) ShowSelectionFrame(bounds.Value);
        }
    }

    private void Track(Point dip, bool final = false)
    {
        var physical = _coordinateMapper.ToPhysical(dip);
        var accepted = final ? _sampler.AddFinal(physical) : _sampler.Add(physical);
        if (!accepted) return;
        _stroke.Add(dip);
        _visual.Halo.Points.Add(dip);
        _visual.Accent.Points.Add(dip);
        QueueRevealUpdate();
    }

    private void QueueRevealUpdate()
    {
        if (_revealUpdateQueued || _disposed) return;
        _revealUpdateQueued = true;
        CompositionTarget.Rendering += FlushReveal;
    }

    private void UnqueueRevealUpdate()
    {
        if (!_revealUpdateQueued) return;
        _revealUpdateQueued = false;
        CompositionTarget.Rendering -= FlushReveal;
    }

    private void FlushReveal(object? sender, EventArgs e)
    {
        UnqueueRevealUpdate();
        if (_disposed) return;
        var size = new Size(_coordinateRoot.ActualWidth, _coordinateRoot.ActualHeight);
        _visual.Dim.Data = SelectionOverlayTransitions.BuildRevealGeometry(size, _stroke);
        _visual.Sheen.Data = SelectionOverlayTransitions.BuildPolygonGeometry(_stroke);
    }

    private void ReleaseMouseCapture()
    {
        if (ReferenceEquals(Mouse.Captured, _visual.InputSurface)) Mouse.Capture(null);
    }

    private void OnHoldCompleted(object? sender, EventArgs e)
    {
        StopHoldTimer();
        if (!_disposed) _holdCompleted();
    }

    private void StopHoldTimer()
    {
        if (_holdTimer is null) return;
        _holdTimer.Stop();
        _holdTimer.Tick -= OnHoldCompleted;
        _holdTimer = null;
    }

    private void ResetSelectionGesture()
    {
        UnqueueRevealUpdate();
        StopHoldTimer();
        _sampler.Reset();
        _stroke.Clear();
        _visual.Halo.Points.Clear();
        _visual.Accent.Points.Clear();
        var size = new Size(_coordinateRoot.ActualWidth, _coordinateRoot.ActualHeight);
        _visual.Dim.Data = SelectionOverlayTransitions.BuildRevealGeometry(size, []);
        _visual.Sheen.Data = Geometry.Empty;
        _visual.DimRect.Data = Geometry.Empty;
        _visual.SelectionFrame.Data = Geometry.Empty;

        UIElement[] visible = [_visual.Dim, _visual.Sheen, _visual.Halo, _visual.Accent];
        UIElement[] hidden = [_visual.DimRect, _visual.SelectionFrame];
        foreach (var layer in visible)
        {
            layer.BeginAnimation(UIElement.OpacityProperty, null);
            layer.Opacity = 1;
        }
        foreach (var layer in hidden)
        {
            layer.BeginAnimation(UIElement.OpacityProperty, null);
            layer.Opacity = 0;
        }
    }

    private static BitmapSource CreateFrozenFrame(System.Drawing.Bitmap frame, GdiRectangle bounds)
    {
        var cropRect = GdiRectangle.Intersect(bounds, new GdiRectangle(0, 0, frame.Width, frame.Height));
        using var cropped = frame.Clone(cropRect, frame.PixelFormat);
        var hbmp = cropped.GetHbitmap();
        try
        {
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hbmp,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            NativeMethods.DeleteObject(hbmp);
        }
    }

    private static CubicEase EaseOut() => new() { EasingMode = EasingMode.EaseOut };
}
