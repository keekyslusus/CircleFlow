using System.Runtime.InteropServices;

namespace CircleToSearch.Interop;

// message-only window on a dedicated STA thread. RegisterHotKey is thread-bound: it must be
// called from the same thread that owns the window, and WM_HOTKEY arrives on that thread's pump.
public sealed class HotkeyWindow : IDisposable
{
    private const string ClassName = "CircleToSearch_HotkeyWindow";
    private const int HotkeyId = 1;
    private const int ErrorClassAlreadyExists = 1410;

    private static readonly Lock RegistrationGate = new();
    private static readonly Dictionary<IntPtr, HotkeyWindow> Windows = [];
    private static WndProc? _windowProc;

    private readonly StaDispatcher _dispatcher;
    private readonly PluginLog _log;
    private readonly IntPtr _hwnd;
    private bool _disposed;

    public event Action? HotkeyPressed;

    public HotkeyWindow(PluginLog log)
    {
        _log = log;
        _dispatcher = new StaDispatcher("CircleToSearch hotkey");
        if (!EnsureWindowClass())
        {
            _log.Warn(nameof(HotkeyWindow), "window class registration failed; the global hotkey is unavailable");
            return;
        }

        var hwnd = IntPtr.Zero;
        var createError = 0;
        _dispatcher.Send(() =>
        {
            hwnd = NativeMethods.CreateWindowExW(
                0,
                ClassName,
                "CircleToSearch hotkey",
                0,
                0,
                0,
                0,
                0,
                NativeMethods.HWND_MESSAGE,
                IntPtr.Zero,
                NativeMethods.GetModuleHandleW(null),
                IntPtr.Zero);
            if (hwnd == IntPtr.Zero) createError = Marshal.GetLastWin32Error();
        });

        if (hwnd == IntPtr.Zero)
        {
            _log.Warn(nameof(HotkeyWindow), $"message-only window creation failed (error {createError})");
            return;
        }

        _hwnd = hwnd;
        lock (RegistrationGate) Windows[_hwnd] = this;
    }

    public bool TryRegister(uint modifiers, uint virtualKey, out int errorCode)
    {
        var registered = false;
        var lastError = 0;
        if (_hwnd != IntPtr.Zero)
        {
            _dispatcher.Send(() =>
            {
                registered = NativeMethods.RegisterHotKey(_hwnd, HotkeyId, modifiers, virtualKey);
                if (!registered) lastError = Marshal.GetLastWin32Error();
            });
        }
        errorCode = lastError;
        return registered;
    }

    public void TryUnregister()
    {
        if (_hwnd == IntPtr.Zero) return;
        _dispatcher.Send(() => NativeMethods.UnregisterHotKey(_hwnd, HotkeyId));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hwnd != IntPtr.Zero)
        {
            _dispatcher.Send(() =>
            {
                NativeMethods.UnregisterHotKey(_hwnd, HotkeyId);
                NativeMethods.DestroyWindow(_hwnd);
            });
            lock (RegistrationGate) Windows.Remove(_hwnd);
        }
        _dispatcher.Dispose();
    }

    private static bool EnsureWindowClass()
    {
        lock (RegistrationGate)
        {
            if (_windowProc is not null) return true;

            _windowProc = WindowProcBridge;
            var windowClass = new WNDCLASSW
            {
                WndProc = Marshal.GetFunctionPointerForDelegate(_windowProc),
                Instance = NativeMethods.GetModuleHandleW(null),
                ClassName = ClassName,
            };
            var atom = NativeMethods.RegisterClassW(ref windowClass);
            if (atom != 0) return true;

            var error = Marshal.GetLastWin32Error();
            if (error == ErrorClassAlreadyExists) return true;

            return false;
        }
    }

    private static IntPtr WindowProcBridge(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == NativeMethods.WM_HOTKEY)
        {
            HotkeyWindow? window;
            lock (RegistrationGate) Windows.TryGetValue(hwnd, out window);
            if (window is not null)
            {
                try
                {
                    window.HotkeyPressed?.Invoke();
                    return IntPtr.Zero;
                }
                catch (Exception exception)
                {
                    window._log.Error(nameof(HotkeyWindow), "hotkey callback failed", exception);
                    return IntPtr.Zero;
                }
            }
        }
        return NativeMethods.DefWindowProcW(hwnd, message, wParam, lParam);
    }
}
