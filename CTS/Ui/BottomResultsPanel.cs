using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CircleToSearch.Interop;
using Rectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Ui;

internal sealed class BottomResultsPanel
{
    private const uint MonitorDefaultToNearest = 2;
    private const uint AbmGetAutoHideBarEx = 0xB;
    private const uint BottomEdge = 3;
    private const int DwmwaWindowCornerPreference = 33;
    private const uint SwpNoZOrderOrActivate = 0x14;
    private const int WmSettingChange = 0x001A;
    private const int WmDisplayChange = 0x007E;
    private const int WmDpiChanged = 0x02E0;
    private readonly Window _window;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private IntPtr _monitor;
    private IntPtr _hwnd;
    private HwndSource? _source;
    private Rectangle _target;
    private bool _positioning;

    internal BottomResultsPanel(Window window, POINT anchor)
    {
        _window = window;
        _monitor = NativeMethods.MonitorFromPoint(anchor, MonitorDefaultToNearest);
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render,
            (_, _) => Tick(), window.Dispatcher);
        _timer.Stop();
        window.SourceInitialized += OnSourceInitialized;
        window.Closed += OnClosed;
    }

    internal static Rectangle CalculateBounds(Rectangle monitor, Rectangle work, double scale, bool autoHideBottom)
    {
        // An auto-hidden taskbar overlays the sheet; its visibility must never move the sheet.
        if (autoHideBottom) work = Rectangle.FromLTRB(work.Left, work.Top, work.Right, monitor.Bottom);
        var gap = Math.Max(1, (int)Math.Round(12 * scale));
        var width = Math.Max(1, Math.Min((int)Math.Round(1200 * scale), work.Width - 2 * gap));
        var height = Math.Max(1, Math.Min((int)Math.Round(820 * scale), (int)Math.Round(work.Height * .78)));
        height = Math.Min(height, Math.Max(1, work.Height - 2 * gap));
        return new Rectangle(work.Left + (work.Width - width) / 2, work.Bottom - gap - height, width, height);
    }

    internal void MoveTo(POINT anchor)
    {
        _monitor = NativeMethods.MonitorFromPoint(anchor, MonitorDefaultToNearest);
        _timer.Stop();
        RefreshBounds();
        Position(_target.Top);
    }

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        _hwnd = new WindowInteropHelper(_window).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(OnMessage);
        RefreshBounds();
        var round = 2;
        NativeMethods.DwmSetWindowAttribute(_hwnd, DwmwaWindowCornerPreference, ref round, sizeof(int));
        if (SystemParameters.ClientAreaAnimation)
        {
            Position(_target.Bottom);
            _clock.Restart();
            _timer.Start();
        }
        else Position(_target.Top);
    }

    private void RefreshBounds()
    {
        var info = new MONITORINFO { CbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!NativeMethods.GetMonitorInfoW(_monitor, ref info))
        {
            _monitor = NativeMethods.MonitorFromPoint(new POINT(), MonitorDefaultToNearest);
            if (!NativeMethods.GetMonitorInfoW(_monitor, ref info)) return;
        }
        var appbar = new AppBarData
        {
            Size = Marshal.SizeOf<AppBarData>(), Edge = BottomEdge, Bounds = info.Monitor,
        };
        var autoHideBottom = SHAppBarMessage(AbmGetAutoHideBarEx, ref appbar) != IntPtr.Zero;
        if (NativeMethods.GetDpiForMonitor(_monitor, 0, out var dpi, out _) != 0 || dpi == 0) dpi = 96;
        _target = CalculateBounds(ToRectangle(info.Monitor), ToRectangle(info.Work), dpi / 96d, autoHideBottom);
    }

    private void Tick()
    {
        var progress = Math.Clamp(_clock.Elapsed.TotalMilliseconds / 280, 0, 1);
        var eased = 1 - Math.Pow(1 - progress, 3);
        Position((int)Math.Round(_target.Bottom - _target.Height * eased));
        if (progress >= 1) _timer.Stop();
    }

    private void Position(int top)
    {
        if (_positioning || _target.Width <= 0) return;
        _positioning = true;
        try
        {
            // Clip the travelling HWND so it cannot cover the taskbar or a monitor below this one.
            var visibleHeight = Math.Clamp(_target.Bottom - top, 0, _target.Height);
            var region = visibleHeight < _target.Height
                ? CreateRectRgn(0, 0, _target.Width, visibleHeight) : IntPtr.Zero;
            if (SetWindowRgn(_hwnd, region, true) == 0 && region != IntPtr.Zero)
                NativeMethods.DeleteObject(region);
            SetWindowPos(_hwnd, IntPtr.Zero, _target.Left, top, _target.Width, _target.Height, SwpNoZOrderOrActivate);
        }
        finally { _positioning = false; }
    }

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_positioning && message is WmSettingChange or WmDisplayChange or WmDpiChanged)
        {
            _window.Dispatcher.BeginInvoke(() =>
            {
                if (_source is null) return;
                var previous = _target;
                RefreshBounds();
                if (_target == previous) return;
                _timer.Stop();
                Position(_target.Top);
            });
        }
        return IntPtr.Zero;
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        _timer.Stop();
        _source?.RemoveHook(OnMessage);
        _source = null;
        _window.SourceInitialized -= OnSourceInitialized;
        _window.Closed -= OnClosed;
    }

    private static Rectangle ToRectangle(RECT rect) => Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int Size;
        public IntPtr Hwnd;
        public uint CallbackMessage;
        public uint Edge;
        public RECT Bounds;
        public IntPtr Parameter;
    }

    [DllImport("shell32.dll")]
    private static extern IntPtr SHAppBarMessage(uint message, ref AppBarData data);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
}
