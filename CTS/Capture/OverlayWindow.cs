using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Interop;
using CircleToSearch.Ui;
using GdiBitmap = System.Drawing.Bitmap;
using GdiGraphics = System.Drawing.Graphics;
using GdiPoint = System.Drawing.Point;
using GdiRectangle = System.Drawing.Rectangle;
using GdiSize = System.Drawing.Size;

namespace CircleToSearch.Capture;

public enum OverlayExitFade
{
    // Window-level opacity: turns the window layered mid-flight, which composites black
    // on some setups (reproduced in FadeCaptureTests). Kept only for the capture harness.
    Window,

    // Window created with AllowsTransparency; only the root grid fades. Production default.
    Root,

    // Opaque window; dim/lasso/chip fade away, the frozen frame stays until close.
    DimLayers,
}

public sealed class OverlayWindow : Window
{
    private const uint MonitorDefaultToNearest = 2;
    private const int MonitorEffectiveDpi = 0;
    private const double ChipEdgeMarginDips = 32;
    private const double SampleDistanceDips = 3;
    private static readonly TimeSpan ExitFadeDuration = TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan SelectionHoldDuration = TimeSpan.FromMilliseconds(450);

    private readonly GdiBitmap _frame;
    private readonly GdiRectangle _monitor;
    private readonly double _scale;
    private readonly int _paddingPx;
    private readonly int _minDiagonalPx;
    private readonly OverlayVisual _visual;
    private readonly LassoPathSampler _sampler;
    private readonly OverlayExitFade _exitFade;
    private readonly bool _clickThroughOnCancel;
    private readonly bool _overscan;
    private readonly List<Point> _stroke = [];
    private bool _drawing;
    private bool _finished;
    private bool _chipDismissed;
    private bool _revealUpdateQueued;

    public SelectionOutcome? Outcome { get; private set; }

    internal OverlayWindow(
        GdiBitmap frame,
        GdiRectangle monitor,
        GdiRectangle workArea,
        double scale,
        OverlayOptions options,
        UiStrings strings,
        bool allowsTransparency = true,
        OverlayExitFade exitFade = OverlayExitFade.Root,
        bool clickThroughOnCancel = true,
        bool overscan = true)
    {
        _frame = frame;
        _monitor = monitor;
        _scale = scale;
        _paddingPx = options.PaddingPx;
        _minDiagonalPx = options.MinDiagonalPx;
        _exitFade = exitFade;
        _clickThroughOnCancel = clickThroughOnCancel;
        _overscan = overscan;
        _sampler = new LassoPathSampler(SampleDistanceDips * scale);

        Title = strings.PluginTitle;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = true;
        Cursor = Cursors.Cross;
        Left = monitor.Left / scale;
        Top = monitor.Top / scale;
        Width = monitor.Width / scale;
        Height = monitor.Height / scale;
        // Born transparent: a window that becomes layered later (window Opacity < 1) renders
        // black on this setup; a transparent window fades its content cleanly into the desktop.
        if (allowsTransparency)
        {
            AllowsTransparency = true;
            Background = Brushes.Transparent;
        }
        else
        {
            Background = CreateFrozenSolidBrush(Colors.Black);
        }
        if (overscan)
        {
            // The window rect then differs from the monitor rect, which keeps DWM's
            // fullscreen-cover heuristics (global shadow disabling) idle; the 1 DIP
            // overhang on each side hangs off the screen and is never visible.
            Left -= 1;
            Top -= 1;
            Width += 2;
            Height += 2;
        }

        _visual = OverlayVisualFactory.CreateRoot(
            CreateFrozenFrame(frame),
            new Size(Width, Height),
            ChipBottomMargin(monitor, workArea, scale) + (overscan ? 1 : 0),
            strings);
        if (overscan) _visual.Screenshot.Margin = new Thickness(1);
        Content = _visual.Root;
        Loaded += OnLoaded;

        PreviewKeyDown += OnPreviewKeyDown;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseRightButtonDown += OnMouseRightButtonDown;
        Deactivated += OnDeactivated;
    }

    // Must not be called from an MTA thread: it creates the STA thread that owns the overlay.
    // A null outcome means the selection was canceled.
    public static Task<SelectionOutcome?> SelectAsync(
        PluginLog log,
        OverlayOptions options,
        UiStrings strings,
        CancellationToken cancel)
    {
        var completion = new TaskCompletionSource<SelectionOutcome?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(RunOnce(log, options, strings, cancel));
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "CircleToSearch overlay",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    public void CancelFromCoordinator() => Dispatcher.BeginInvoke(new Action(CancelInternal));

    private static SelectionOutcome? RunOnce(
        PluginLog log,
        OverlayOptions options,
        UiStrings strings,
        CancellationToken cancel)
    {
        var previousContext = NativeMethods.SetThreadDpiAwarenessContext(NativeMethods.DpiAwarenessPerMonitorV2);
        try
        {
            if (!TryCapturePointerMonitor(out var monitor, out var workArea, out var frame, out var scale))
            {
                log.Warn(nameof(OverlayWindow), "capturing the pointer monitor failed; selection canceled");
                return null;
            }

            if (cancel.IsCancellationRequested)
            {
                frame.Dispose();
                return null;
            }

            SelectionOutcome? outcome = null;
            try
            {
                var window = new OverlayWindow(frame, monitor, workArea, scale, options, strings);
                window.Show();
                using var registration = cancel.Register(window.CancelFromCoordinator);
                Dispatcher.Run();
                outcome = window.Outcome;
                return outcome;
            }
            finally
            {
                if (outcome is null) frame.Dispose();
            }
        }
        finally
        {
            NativeMethods.SetThreadDpiAwarenessContext(previousContext);
        }
    }

    private static bool TryCapturePointerMonitor(
        out GdiRectangle monitor,
        out GdiRectangle workArea,
        out GdiBitmap frame,
        out double scale)
    {
        monitor = default;
        workArea = default;
        frame = null!;
        scale = 1.0;

        if (!NativeMethods.GetCursorPos(out var pointer)) return false;
        var handle = NativeMethods.MonitorFromPoint(pointer, MonitorDefaultToNearest);
        if (handle == IntPtr.Zero) return false;

        var info = new MONITORINFO { CbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfoW(handle, ref info)) return false;

        var bounds = info.Monitor;
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        if (width <= 0 || height <= 0) return false;

        if (NativeMethods.GetDpiForMonitor(handle, MonitorEffectiveDpi, out var dpiX, out _) != 0 || dpiX == 0)
            dpiX = 96;
        scale = dpiX / 96.0;

        var captured = new GdiBitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = GdiGraphics.FromImage(captured);
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, new GdiSize(width, height));
        }
        catch
        {
            captured.Dispose();
            throw;
        }

        var work = info.Work;
        monitor = new GdiRectangle(bounds.Left, bounds.Top, width, height);
        workArea = new GdiRectangle(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top);
        frame = captured;
        return true;
    }

    private static BitmapSource CreateFrozenFrame(GdiBitmap frame)
    {
        var hbmp = frame.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(
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

    private static SolidColorBrush CreateFrozenSolidBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static double ChipBottomMargin(GdiRectangle monitor, GdiRectangle workArea, double scale) =>
        (monitor.Bottom - workArea.Bottom) / scale + ChipEdgeMarginDips;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_chipDismissed) OverlayVisualFactory.BeginChipEntrance(_visual);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        CancelInternal();
        e.Handled = true;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_finished) return;
        _drawing = true;
        _chipDismissed = true;
        OverlayVisualFactory.BeginChipExit(_visual);
        _sampler.Reset();
        _stroke.Clear();
        Track(e);
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_drawing || _finished) return;
        Track(e);
        e.Handled = true;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_drawing || _finished) return;
        _drawing = false;
        ReleaseMouseCapture();
        Track(e, final: true);
        var bounds = LassoBoundsCalculator.Calculate(_sampler.Points, _monitor, _paddingPx, _minDiagonalPx);
        if (bounds is null)
        {
            CancelInternal();
        }
        else
        {
            _finished = true;
            Outcome = new SelectionOutcome(bounds.Value, _frame);
            ShowSelectionFrame(bounds.Value);
        }
        e.Handled = true;
    }

    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        CancelInternal();
        e.Handled = true;
    }

    private void OnDeactivated(object? sender, EventArgs e) => CancelInternal();

    private void Track(MouseEventArgs e, bool final = false)
    {
        var dip = e.GetPosition(this);
        var physical = ToPhysical(dip);
        var accepted = final ? _sampler.AddFinal(physical) : _sampler.Add(physical);
        if (!accepted) return;
        _stroke.Add(dip);
        _visual.Halo.Points.Add(dip);
        _visual.Accent.Points.Add(dip);
        QueueRevealUpdate();
    }

    private void QueueRevealUpdate()
    {
        if (_revealUpdateQueued) return;
        _revealUpdateQueued = true;
        CompositionTarget.Rendering += FlushReveal;
    }

    private void UnqueueRevealUpdate()
    {
        if (!_revealUpdateQueued) return;
        _revealUpdateQueued = false;
        CompositionTarget.Rendering -= FlushReveal;
    }

    // Runs at most once per render frame, so fast drags never rebuild the mask more often than displayed.
    private void FlushReveal(object? sender, EventArgs e)
    {
        _revealUpdateQueued = false;
        CompositionTarget.Rendering -= FlushReveal;
        var size = new Size(ActualWidth, ActualHeight);
        _visual.Dim.Data = OverlayVisualFactory.BuildRevealGeometry(size, _stroke);
        _visual.Sheen.Data = OverlayVisualFactory.BuildPolygonGeometry(_stroke);
    }

    // Circle-to-search style finish: the lasso snaps into the exact rectangle that will be
    // sent, the frame holds for a beat so the region stays readable, then the window closes
    // into the provider with no fade (the frozen frame matches the live desktop).
    private void ShowSelectionFrame(GdiRectangle bounds)
    {
        UnqueueRevealUpdate();
        var size = new Size(ActualWidth, ActualHeight);
        var offset = _overscan ? 1 : 0;
        var rect = new Rect(
            bounds.Left / _scale + offset,
            bounds.Top / _scale + offset,
            bounds.Width / _scale,
            bounds.Height / _scale);
        Point[] corners =
        [
            new(rect.Left, rect.Top),
            new(rect.Right, rect.Top),
            new(rect.Right, rect.Bottom),
            new(rect.Left, rect.Bottom),
        ];
        OverlayVisualFactory.BeginSelectionReveal(
            _visual,
            OverlayVisualFactory.BuildRevealGeometry(size, corners),
            OverlayVisualFactory.BuildSelectionFrameGeometry(rect));

        var hold = new DispatcherTimer { Interval = SelectionHoldDuration };
        hold.Tick += (_, _) =>
        {
            hold.Stop();
            FinishShutdown();
        };
        hold.Start();
    }

    private GdiPoint ToPhysical(Point dip)
        => new((int)Math.Round(dip.X * _scale), (int)Math.Round(dip.Y * _scale));

    private void CancelInternal()
    {
        if (_finished) return;
        _finished = true;
        UnqueueRevealUpdate();
        ReleaseMouseCapture();
        if (!OverlayVisualFactory.AnimationsEnabled())
        {
            Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            return;
        }

        // The frozen frame cross-fades into the live desktop; only the cancel path animates,
        // the mouse-up path shows the selection rectangle and closes without a window fade.
        var fade = Fade(1, 0);
        switch (_exitFade)
        {
            case OverlayExitFade.Root:
                fade.Completed += (_, _) => Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                _visual.Root.BeginAnimation(OpacityProperty, fade);
                break;
            case OverlayExitFade.DimLayers:
                // No window transparency: the overlay layers melt away while the frozen
                // frame (identical to the live desktop) stays until close.
                var completed = new DoubleAnimation(1, 0, ExitFadeDuration) { EasingFunction = EaseOut() };
                completed.Completed += (_, _) => Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                _visual.Dim.BeginAnimation(OpacityProperty, completed);
                _visual.Sheen.BeginAnimation(OpacityProperty, Fade(1, 0));
                _visual.Halo.BeginAnimation(OpacityProperty, Fade(1, 0));
                _visual.Accent.BeginAnimation(OpacityProperty, Fade(1, 0));
                OverlayVisualFactory.BeginChipExit(_visual);
                break;
            default:
                fade.Completed += (_, _) => Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                BeginAnimation(OpacityProperty, fade);
                break;
        }

        if (_clickThroughOnCancel) MakeClickThrough();
        IsHitTestVisible = false;
    }

    private DoubleAnimation Fade(double from, double to) =>
        new(from, to, ExitFadeDuration) { EasingFunction = EaseOut() };

    private static CubicEase EaseOut() => new() { EasingMode = EasingMode.EaseOut };

    private void MakeClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var style = NativeMethods.GetWindowLongW(hwnd, NativeMethods.GwlExStyle);
        // Transparent only: setting WS_EX_LAYERED by hand detaches WPF's DWM redirection
        // surface and the window renders black; WPF enables layering itself for Opacity < 1.
        NativeMethods.SetWindowLongW(hwnd, NativeMethods.GwlExStyle, style | NativeMethods.WsExTransparent);
    }

    private void FinishShutdown()
    {
        _finished = true;
        UnqueueRevealUpdate();
        Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
    }
}
