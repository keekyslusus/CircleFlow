using System.Windows;
using System.Windows.Threading;

namespace CircleToSearch.Shell;

internal sealed class SingleWindowController(Dispatcher dispatcher, Func<Window> createWindow) : IDisposable
{
    private Window? _window;
    private bool _disposed;
    internal Window? CurrentWindow => _window;

    public void Show()
    {
        dispatcher.VerifyAccess();
        if (_disposed) return;
        if (_window is null)
        {
            var window = createWindow();
            window.Closed += (_, _) => { if (ReferenceEquals(_window, window)) _window = null; };
            _window = window;
        }
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Show();
        _window.Activate();
    }

    public void Dispose()
    {
        dispatcher.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        _window?.Close();
        _window = null;
    }
}
