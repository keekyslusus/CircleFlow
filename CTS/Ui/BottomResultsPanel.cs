using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using CircleToSearch.Interop;
using Rectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Ui;

internal sealed class BottomResultsPanel
{
    private const uint MonitorDefaultToNull = 0;
    private const uint MonitorDefaultToNearest = 2;
    private const uint AbmGetAutoHideBarEx = 0xB;
    private const uint BottomEdge = 3;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaCloak = 13;
    private const uint SwpNoZOrderOrActivate = 0x14;
    private const int WmSettingChange = 0x001A;
    private const int WmDisplayChange = 0x007E;
    private const int WmDpiChanged = 0x02E0;
    private readonly Window _window;
    private readonly bool _animationsEnabled;
    private TaskCompletionSource _entrance = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Stopwatch _clock = new();
    private readonly KeySpline _entranceSpline = new(0.34, 0.88, 0.34, 1.00);
    private bool _rendering;
    private IntPtr _monitor;
    private IntPtr _hwnd;
    private HwndSource? _source;
    private Rectangle _target;
    private int _entranceTop;
    private bool _positioning;
    private bool _cloaked;
    private bool _countingFrames;
    private int _framesSeen;
    // WPF has put a frame on screen, so revealing the window never shows its unpainted surface.
    private bool _presented;
    private bool _waitingForFirstFrame;

    internal BottomResultsPanel(Window window, POINT anchor, bool? animationsEnabled = null)
    {
        _window = window;
        _monitor = NativeMethods.MonitorFromPoint(anchor, MonitorDefaultToNearest);
        _animationsEnabled = animationsEnabled ?? SystemParameters.ClientAreaAnimation;
        // Preserve the native caption style required by DWM, while drawing our own header.
        window.WindowStyle = WindowStyle.SingleBorderWindow;
        WindowChrome.SetWindowChrome(window, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(0),
            GlassFrameThickness = new Thickness(1),
            UseAeroCaptionButtons = false,
        });
        window.SourceInitialized += OnSourceInitialized;
        window.Closing += OnClosing;
        window.Closed += OnClosed;
    }

    internal Task EntranceCompleted => _entrance.Task;

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

    // A window region would make DWM fall back to the basic frame, so the sheet is not clipped: it rises from
    // the screen edge behind the topmost taskbar, or only from its own gap when another monitor lies below.
    internal static int CalculateEntranceTop(Rectangle monitor, Rectangle target, bool monitorBelow) =>
        monitorBelow ? target.Top + monitor.Bottom - target.Bottom : monitor.Bottom;

    internal void MoveTo(POINT anchor)
    {
        _monitor = NativeMethods.MonitorFromPoint(anchor, MonitorDefaultToNearest);
        FinishEntrance();
        RefreshBounds();
        Position(_target.Top);
    }

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        _hwnd = new WindowInteropHelper(_window).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(OnMessage);
        SetNativeTransitions(false);
        RefreshBounds();
        var round = 2;
        NativeMethods.DwmSetWindowAttribute(_hwnd, DwmwaWindowCornerPreference, ref round, sizeof(int));
        Position(_animationsEnabled ? _entranceTop : _target.Top);
    }

    internal void ShowHidden()
    {
        // A cloaked window keeps rendering for WebView2 without being visible, clickable or active.
        _window.ShowActivated = false;
        SetNativeTransitions(false);
        Show(cloaked: true);
        Position(_target.Top);
    }

    internal Task ShowAsync()
    {
        _window.ShowActivated = true;
        SetNativeTransitions(false);
        if (_rendering || _waitingForFirstFrame)
        {
            Show(_cloaked);
            return _entrance.Task;
        }
        if (_entrance.Task.IsCompleted)
        {
            _entrance = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _clock.Reset();
        }
        Show(cloaked: !_presented);
        if (_presented) BeginEntrance();
        else _waitingForFirstFrame = true;
        return _entrance.Task;
    }

    private void Show(bool cloaked)
    {
        new WindowInteropHelper(_window).EnsureHandle();
        SetCloaked(cloaked);
        _window.Show();
        if (_presented || _countingFrames) return;
        _countingFrames = true;
        CompositionTarget.Rendering += OnFirstFrames;
    }

    private void OnFirstFrames(object? sender, EventArgs args)
    {
        // The first tick renders the first frame; by the next one it has been presented.
        if (++_framesSeen < 2) return;
        StopCountingFrames();
        _presented = true;
        if (!_waitingForFirstFrame) return;
        _waitingForFirstFrame = false;
        BeginEntrance();
    }

    private void StopCountingFrames()
    {
        if (_countingFrames) CompositionTarget.Rendering -= OnFirstFrames;
        _countingFrames = false;
    }

    private void BeginEntrance()
    {
        if (_animationsEnabled)
        {
            Position(_entranceTop);
            SetCloaked(false);
            _rendering = true;
            CompositionTarget.Rendering += OnRendering;
        }
        else
        {
            Position(_target.Top);
            SetCloaked(false);
            FinishEntrance();
        }
    }

    private void SetCloaked(bool cloaked)
    {
        if (_hwnd == IntPtr.Zero || _cloaked == cloaked) return;
        var value = cloaked ? 1 : 0;
        if (NativeMethods.DwmSetWindowAttribute(_hwnd, DwmwaCloak, ref value, sizeof(int)) == 0) _cloaked = cloaked;
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
        var monitor = ToRectangle(info.Monitor);
        _target = CalculateBounds(monitor, ToRectangle(info.Work), dpi / 96d, autoHideBottom);
        var below = new RECT
        {
            Left = _target.Left, Top = monitor.Bottom, Right = _target.Right, Bottom = monitor.Bottom + _target.Height,
        };
        _entranceTop = CalculateEntranceTop(monitor, _target,
            MonitorFromRect(ref below, MonitorDefaultToNull) != IntPtr.Zero);
    }

    private void OnRendering(object? sender, EventArgs args)
    {
        // Start the clock on the first actual frame, not while WPF is constructing the HWND.
        if (!_clock.IsRunning) _clock.Start();
        var progress = Math.Clamp(_clock.Elapsed.TotalMilliseconds / 300, 0, 1);
        var eased = _entranceSpline.GetSplineProgress(progress);
        Position((int)Math.Round(_entranceTop - (_entranceTop - _target.Top) * eased));
        if (progress >= 1) FinishEntrance();
    }

    private void FinishEntrance()
    {
        if (_rendering) CompositionTarget.Rendering -= OnRendering;
        _rendering = false;
        SetNativeTransitions(_animationsEnabled);
        _entrance.TrySetResult();
    }

    private void SetNativeTransitions(bool enabled)
    {
        if (_hwnd == IntPtr.Zero) return;
        var disabled = enabled ? 0 : 1;
        NativeMethods.DwmSetWindowAttribute(_hwnd, NativeMethods.DwmwaTransitionsForceDisabled, ref disabled, sizeof(int));
    }

    private void OnClosing(object? sender, CancelEventArgs args) => FinishEntrance();

    private void Position(int top)
    {
        if (_positioning || _target.Width <= 0) return;
        _positioning = true;
        try
        {
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
                if (_rendering) return;
                Position(_animationsEnabled && !_entrance.Task.IsCompleted ? _entranceTop : _target.Top);
            });
        }
        return IntPtr.Zero;
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        StopCountingFrames();
        _waitingForFirstFrame = false;
        FinishEntrance();
        _source?.RemoveHook(OnMessage);
        _source = null;
        _window.SourceInitialized -= OnSourceInitialized;
        _window.Closing -= OnClosing;
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
    private static extern IntPtr MonitorFromRect(ref RECT rect, uint flags);
}
