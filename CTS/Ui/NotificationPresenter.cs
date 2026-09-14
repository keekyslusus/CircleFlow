using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace CircleToSearch.Ui;

internal sealed class NotificationPresenter(Dispatcher dispatcher, UiStrings strings, PluginLog log) : IDisposable
{
    private readonly List<Window> _windows = [];
    private bool _disposed;
    internal IReadOnlyList<Window> Windows => _windows.AsReadOnly();

    public void ShowMessage(string title, string message) => Post(title, message, null, null);
    public void ShowError(string title, string message) => Post(title, message, null, null);
    public void ShowMessageWithButton(string title, string message, string button, Action action) =>
        Post(title, message, button, action);

    private void Post(string title, string message, string? button, Action? action)
    {
        if (dispatcher.HasShutdownStarted) return;
        dispatcher.BeginInvoke(new Action(() =>
        {
            if (_disposed) return;
            try { Show(title, message, button, action); }
            catch (Exception exception) { log.SafeError(nameof(NotificationPresenter), "show-notification", exception); }
        }));
    }

    private void Show(string title, string message, string? buttonText, Action? action)
    {
        var palette = PluginPalette.For(SystemTheme.IsLight());
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxHeight = 400 });
        var window = new Window
        {
            Title = title, Width = 400, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, Topmost = true,
            Background = new SolidColorBrush(palette.WindowSurface), Foreground = new SolidColorBrush(palette.PrimaryText),
            Content = panel, WindowStartupLocation = WindowStartupLocation.Manual,
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0) };
        if (action is not null && buttonText is not null)
        {
            var button = new Button { Content = buttonText, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 8, 0) };
            var invoked = false;
            button.Click += (_, _) =>
            {
                if (_disposed || invoked) return;
                invoked = true;
                window.Close();
                try { action(); }
                catch (Exception exception) { log.SafeError(nameof(NotificationPresenter), "notification-action", exception); }
            };
            buttons.Children.Add(button);
        }
        var close = new Button { Content = strings.Close, Padding = new Thickness(12, 5, 12, 5) };
        close.Click += (_, _) => window.Close();
        buttons.Children.Add(close);
        panel.Children.Add(buttons);
        window.Closed += (_, _) => _windows.Remove(window);
        window.Loaded += (_, _) =>
        {
            var work = SystemParameters.WorkArea;
            window.Left = Math.Max(work.Left, work.Right - window.ActualWidth - 16);
            window.Top = Math.Max(work.Top, work.Bottom - window.ActualHeight - 16 - (_windows.Count - 1) * 40);
        };
        _windows.Add(window);
        try { window.Show(); }
        catch { _windows.Remove(window); window.Close(); throw; }
    }

    public void CloseAll()
    {
        dispatcher.VerifyAccess();
        var failures = new List<Exception>();
        foreach (var window in _windows.ToArray())
        {
            try { window.Close(); }
            catch (Exception exception) { failures.Add(exception); }
        }
        if (failures.Count != 0) throw new AggregateException(failures);
    }

    public void Dispose()
    {
        dispatcher.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        CloseAll();
    }
}
