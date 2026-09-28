using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CircleToSearch.Shell.SettingsPreview;
using CircleToSearch.Ui;

namespace CircleToSearch.Shell.Notifications;

internal sealed class NotificationPresenter(
    Dispatcher dispatcher,
    UiStrings strings,
    string iconPath,
    Func<bool> lightTheme,
    PluginLog log,
    TimeSpan? autoDismiss = null) : IDisposable
{
    internal static readonly TimeSpan DefaultAutoDismiss = TimeSpan.FromSeconds(6);
    private readonly List<NotificationCard> _cards = [];
    private Window? _window;
    private StackPanel? _stack;
    private NotificationChrome? _chrome;
    private bool _disposed;

    internal IReadOnlyList<NotificationCard> Cards => _cards.AsReadOnly();
    internal Window? Window => _window;

    public void ShowMessage(string title, string message) => Post(title, message, null, null, isError: false);
    public void ShowError(string title, string message) => Post(title, message, null, null, isError: true);
    public void ShowMessageWithButton(string title, string message, string button, Action action) =>
        Post(title, message, button, action, isError: false);

    private void Post(string title, string message, string? button, Action? action, bool isError)
    {
        if (dispatcher.HasShutdownStarted) return;
        dispatcher.BeginInvoke(new Action(() =>
        {
            if (_disposed) return;
            try { Show(title, message, button, action, isError); }
            catch (Exception exception) { log.SafeError(nameof(NotificationPresenter), "show-notification", exception); }
        }));
    }

    private void Show(string title, string message, string? button, Action? action, bool isError)
    {
        // Most callers pass the app name as the title, which the card header already shows.
        var content = new NotificationContent(title == strings.PluginTitle ? null : title, message, button, isError);
        var waitsForUser = action is not null || isError;
        // The daily update check and repeated failures would otherwise stack identical cards the user has not closed.
        if (waitsForUser && _cards.Any(shown => shown.Content == content)) return;
        var (window, stack, chrome) = EnsureWindow();
        NotificationCard? card = null;
        try
        {
            card = new NotificationCard(window, content, chrome,
                close: () => Remove(card!),
                act: action is null ? null : () =>
                {
                    if (!Remove(card!)) return;
                    try { action(); }
                    catch (Exception exception) { log.SafeError(nameof(NotificationPresenter), "notification-action", exception); }
                },
                // Errors and offers wait for the user; a plain message is only information.
                autoDismiss: waitsForUser ? null : autoDismiss ?? DefaultAutoDismiss);
        }
        catch
        {
            if (stack.Children.Count == 0) CloseWindow();
            throw;
        }
        _cards.Add(card);
        stack.Children.Add(card.Root);
        if (!window.IsVisible) window.Show();
        card.Enter();
    }

    private (Window Window, StackPanel Stack, NotificationChrome Chrome) EnsureWindow()
    {
        if (_window is not null) return (_window, _stack!, _chrome!);
        var light = lightTheme();
        var window = (Window)Application.LoadComponent(new Uri(
            "/CircleFlow;component/CTS/Shell/Notifications/NotificationWindow.xaml", UriKind.Relative));
        SettingsWindowTheme.Apply(window, light, iconPath);
        window.Width = NotificationCard.CardWidth + 32;
        Place(window);
        // An error can stay open while the taskbar moves or the resolution changes.
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        var theme = PluginPalette.For(light);
        _chrome = new NotificationChrome(strings.PluginTitle, strings.Close, window.Icon,
            SettingsWindowTheme.Frozen(theme.Toast.ErrorAccent), theme.StateCard.ShadowOpacity);
        _stack = (StackPanel)window.FindName("Cards");
        _window = window;
        return (window, _stack, _chrome);
    }

    // A transparent column along the right edge: its empty pixels pass clicks through to the desktop.
    private static void Place(Window window)
    {
        var work = SystemParameters.WorkArea;
        window.Height = work.Height;
        window.Left = work.Right - window.Width;
        window.Top = work.Top;
    }

    private void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SystemParameters.WorkArea)) return;
        dispatcher.BeginInvoke(new Action(() => { if (_window is { } window) Place(window); }));
    }

    private bool Remove(NotificationCard card)
    {
        if (_disposed || !_cards.Remove(card)) return false;
        var stack = _stack;
        card.Exit(() =>
        {
            stack?.Children.Remove(card.Root);
            if (ReferenceEquals(stack, _stack) && stack?.Children.Count == 0) CloseWindow();
        });
        return true;
    }

    public void CloseAll()
    {
        dispatcher.VerifyAccess();
        foreach (var card in _cards) card.Stop();
        _cards.Clear();
        CloseWindow();
    }

    private void CloseWindow()
    {
        var window = _window;
        if (window is not null) SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
        _window = null;
        _stack = null;
        _chrome = null;
        window?.Close();
    }

    public void Dispose()
    {
        dispatcher.VerifyAccess();
        if (_disposed) return;
        CloseAll();
        _disposed = true;
    }
}
