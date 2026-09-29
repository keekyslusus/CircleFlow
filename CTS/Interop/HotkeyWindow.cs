using System.Runtime.InteropServices;

namespace CircleToSearch.Interop;

// message-only window on a dedicated STA thread. RegisterHotKey is thread-bound: it must be
// called from the same thread that owns the window, and WM_HOTKEY arrives on that thread's pump.
public sealed class HotkeyWindow : IDisposable, IAsyncDisposable
{
    private const string ClassName = "CircleToSearch_HotkeyWindow";
    private const int HotkeyId = 1;
    private const int ErrorClassAlreadyExists = 1410;

    private static readonly Lock RegistrationGate = new();
    private static readonly Dictionary<IntPtr, HotkeyWindow> Windows = [];
    private static WndProc? _windowProc;

    private readonly IStaDispatcher _dispatcher;
    private readonly PluginLog _log;
    private readonly IntPtr _hwnd;
    private int _disposed;
    private readonly object _stopGate = new();
    private Task? _stopTask;

    public event Action? HotkeyPressed;

    internal HotkeyWindow(IStaDispatcher dispatcher, PluginLog log)
    {
        _dispatcher = dispatcher;
        _log = log;
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
        if (_hwnd != IntPtr.Zero && Volatile.Read(ref _disposed) == 0)
        {
            _dispatcher.Send(() =>
            {
                registered = NativeMethods.RegisterHotKey(
                    _hwnd,
                    HotkeyId,
                    RegistrationModifiers(modifiers),
                    virtualKey);
                if (!registered) lastError = Marshal.GetLastWin32Error();
            });
        }
        errorCode = lastError;
        return registered;
    }

    internal static uint RegistrationModifiers(uint modifiers) =>
        modifiers | NativeMethods.MOD_NOREPEAT;

    public bool TryUnregister()
    {
        if (_hwnd == IntPtr.Zero) return false;
        var unregistered = false;
        _dispatcher.Send(() => unregistered = NativeMethods.UnregisterHotKey(_hwnd, HotkeyId));
        return unregistered;
    }

    public void Dispose()
    {
        try { StopAsync().WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); }
        catch (TimeoutException) { }
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    public Task StopAsync()
    {
        lock (_stopGate)
        {
            if (_stopTask is not null) return _stopTask;
            Interlocked.Exchange(ref _disposed, 1);
            _stopTask = StopCoreAsync();
            return _stopTask;
        }
    }

    private async Task StopCoreAsync()
    {
        if (_hwnd != IntPtr.Zero)
        {
            var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_dispatcher.TryPost(() =>
                {
                    try
                    {
                        NativeMethods.UnregisterHotKey(_hwnd, HotkeyId);
                        NativeMethods.DestroyWindow(_hwnd);
                    }
                    catch (Exception exception)
                    {
                        _log.SafeError(nameof(HotkeyWindow), "destroy-hotkey-window", exception);
                    }
                    finally { cleanup.TrySetResult(); }
                }))
            {
                cleanup.TrySetResult();
            }
            await cleanup.Task.ConfigureAwait(false);
            lock (RegistrationGate) Windows.Remove(_hwnd);
        }
        await _dispatcher.StopAsync().ConfigureAwait(false);
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
            if (window is not null && Volatile.Read(ref window._disposed) == 0)
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
