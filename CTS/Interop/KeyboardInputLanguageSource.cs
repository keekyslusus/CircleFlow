using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using Windows.Globalization;

namespace CircleToSearch.Interop;

public readonly record struct KeyboardLanguageSnapshot(nint Layout, string? Tag);

internal sealed class KeyboardInputLanguageSource : IDisposable
{
    private readonly Action<string?> _changed;
    private readonly Action<string?> _initialReady;
    private HwndSource? _source;
    private Window? _window;
    private string? _tag;
    private nint _initialLayout;
    private bool _synchronizeOnActivation;
    private bool _initialDelivered;
    private bool _disposed;

    internal KeyboardInputLanguageSource(Action<string?> changed, Action<string?> initialReady)
    {
        _changed = changed;
        _initialReady = initialReady;
    }

    internal static KeyboardLanguageSnapshot CaptureForeground()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == IntPtr.Zero) return default;
        var thread = NativeMethods.GetWindowThreadProcessId(window, IntPtr.Zero);
        if (thread == 0) return default;
        var layout = NativeMethods.GetKeyboardLayout(thread);
        return new KeyboardLanguageSnapshot(layout, TagFromLayout(layout));
    }

    internal void Attach(Window window, KeyboardLanguageSnapshot initial)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _source = (HwndSource?)PresentationSource.FromVisual(window)
            ?? throw new InvalidOperationException("The overlay window handle is unavailable.");
        // Synchronize before subscribing so the activation is not reported as a user switch.
        if (initial.Layout != 0)
            NativeMethods.ActivateKeyboardLayout(initial.Layout, 0);
        _initialLayout = initial.Layout;
        _synchronizeOnActivation = !window.IsActive;
        _tag = initial.Tag;
        _source.AddHook(OnMessage);
        _window = window;
        window.Activated += OnActivated;
        if (window.IsActive || initial.Tag is not null)
            Initialize(initial.Tag ?? ReadOwnTag());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_source is null) return;
        if (_window is not null) _window.Activated -= OnActivated;
        _source.RemoveHook(OnMessage);
        _source = null;
        _window = null;
    }

    private IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_initialDelivered && message == NativeMethods.WM_INPUTLANGCHANGE)
            Publish(ReadOwnTag());
        return IntPtr.Zero;
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        if (_synchronizeOnActivation)
        {
            _synchronizeOnActivation = false;
            if (_initialLayout != 0) NativeMethods.ActivateKeyboardLayout(_initialLayout, 0);
            if (!_initialDelivered) Initialize(_tag ?? ReadOwnTag());
            return;
        }
        Publish(ReadOwnTag());
    }

    private void Publish(string? tag)
    {
        if (_disposed || string.Equals(_tag, tag, StringComparison.OrdinalIgnoreCase)) return;
        _tag = tag;
        _changed(tag);
    }

    private void Initialize(string? tag)
    {
        _tag = tag;
        _initialDelivered = true;
        _initialReady(tag);
    }

    private static string? ReadOwnTag()
    {
        var layout = NativeMethods.GetKeyboardLayout(0);
        var tag = TagFromLayout(layout);
        if (tag is not null) return tag;
        try { return Language.CurrentInputMethodLanguageTag; }
        catch { return null; }
    }

    internal static string? TagFromLayout(nint layout)
    {
        if (layout == 0) return null;
        var languageId = unchecked((ushort)(long)layout);
        if (languageId is 0 or 0x1000 || (languageId & 0xF000) == 0x2000) return null;
        try { return CultureInfo.GetCultureInfo(languageId).Name; }
        catch (CultureNotFoundException) { return null; }
    }
}
