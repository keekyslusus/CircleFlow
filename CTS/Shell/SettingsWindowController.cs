using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CircleToSearch.Ui;

namespace CircleToSearch.Shell;

internal sealed class SettingsWindowController(Dispatcher dispatcher, UiStrings strings) : IDisposable
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
            var window = new Window
            {
                Title = strings.SettingsWindowTitle, Width = 720, Height = 480,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(PluginPalette.For(SystemTheme.IsLight()).WindowSurface),
            };
            window.Closed += (_, _) => { if (ReferenceEquals(_window, window)) _window = null; };
            _window = window;
        }
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Show();
        _window.Activate();
    }

    public void Hide()
    {
        dispatcher.VerifyAccess();
        _window?.Hide();
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
