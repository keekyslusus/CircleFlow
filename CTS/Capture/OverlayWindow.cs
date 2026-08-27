using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using CircleToSearch.Interop;
using GdiBitmap = System.Drawing.Bitmap;
using GdiGraphics = System.Drawing.Graphics;
using GdiPoint = System.Drawing.Point;
using GdiRectangle = System.Drawing.Rectangle;
using GdiSize = System.Drawing.Size;

namespace CircleToSearch.Capture;

public sealed class OverlayWindow : Window
{
    private const uint MonitorDefaultToNearest = 2;
    private const int MonitorEffectiveDpi = 0;

    private readonly GdiBitmap _frame;
    private readonly GdiRectangle _monitor;
    private readonly double _scale;
    private readonly int _paddingPx;
    private readonly int _minDiagonalPx;
    private readonly Canvas _canvas = new();
    private readonly Polyline _lasso = new()
    {
        Stroke = Brushes.Yellow,
        StrokeThickness = 2,
        StrokeLineJoin = PenLineJoin.Round,
    };
    private readonly List<GdiPoint> _path = [];
    private bool _drawing;
    private bool _finished;

    public SelectionOutcome? Outcome { get; private set; }

    private OverlayWindow(GdiBitmap frame, GdiRectangle monitor, double scale, OverlayOptions options)
    {
        _frame = frame;
        _monitor = monitor;
        _scale = scale;
        _paddingPx = options.PaddingPx;
        _minDiagonalPx = options.MinDiagonalPx;

        Title = "Circle to Search";
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
        Background = CreateFrozenBackground(frame);

        _canvas.Children.Add(_lasso);
        Content = _canvas;

        PreviewKeyDown += OnPreviewKeyDown;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseRightButtonDown += OnMouseRightButtonDown;
        Deactivated += OnDeactivated;
    }

    // Must not be called from an MTA thread: it creates the STA thread that owns the overlay.
    // A null outcome means the selection was canceled.
    public static Task<SelectionOutcome?> SelectAsync(PluginLog log, OverlayOptions options, CancellationToken cancel)
    {
        var completion = new TaskCompletionSource<SelectionOutcome?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(RunOnce(log, options, cancel));
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

    private static SelectionOutcome? RunOnce(PluginLog log, OverlayOptions options, CancellationToken cancel)
    {
        var previousContext = NativeMethods.SetThreadDpiAwarenessContext(NativeMethods.DpiAwarenessPerMonitorV2);
        try
        {
            if (!TryCapturePointerMonitor(out var monitor, out var frame, out var scale))
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
                var window = new OverlayWindow(frame, monitor, scale, options);
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

    private static bool TryCapturePointerMonitor(out GdiRectangle monitor, out GdiBitmap frame, out double scale)
    {
        monitor = default;
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

        monitor = new GdiRectangle(bounds.Left, bounds.Top, width, height);
        frame = captured;
        return true;
    }

    private static ImageBrush CreateFrozenBackground(GdiBitmap frame)
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
            return new ImageBrush(source);
        }
        finally
        {
            NativeMethods.DeleteObject(hbmp);
        }
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
        _path.Clear();
        _lasso.Points.Clear();
        AddLassoPoint(e);
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_drawing || _finished) return;
        AddLassoPoint(e);
        e.Handled = true;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_drawing || _finished) return;
        _drawing = false;
        ReleaseMouseCapture();
        AddLassoPoint(e);
        var bounds = LassoBoundsCalculator.Calculate(_path, _monitor, _paddingPx, _minDiagonalPx);
        if (bounds is null)
        {
            CancelInternal();
        }
        else
        {
            _finished = true;
            Outcome = new SelectionOutcome(bounds.Value, _frame);
            Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
        }
        e.Handled = true;
    }

    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        CancelInternal();
        e.Handled = true;
    }

    private void OnDeactivated(object? sender, EventArgs e) => CancelInternal();

    private void AddLassoPoint(MouseEventArgs e)
    {
        var dip = e.GetPosition(this);
        _path.Add(ToPhysical(dip));
        _lasso.Points.Add(dip);
    }

    private GdiPoint ToPhysical(Point dip)
        => new((int)Math.Round(dip.X * _scale), (int)Math.Round(dip.Y * _scale));

    private void CancelInternal()
    {
        if (_finished) return;
        _finished = true;
        Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
    }
}
