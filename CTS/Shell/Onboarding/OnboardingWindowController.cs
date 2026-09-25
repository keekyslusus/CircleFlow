using System.Windows;
using System.Windows.Threading;

namespace CircleToSearch.Shell.Onboarding;

internal sealed class OnboardingWindowController : IDisposable
{
    private readonly OnboardingModel _model;
    private readonly SingleWindowController _window;
    private bool _disposed;

    internal OnboardingWindowController(Dispatcher dispatcher, OnboardingModel model, Func<Window> createWindow)
    {
        _model = model;
        _window = new SingleWindowController(dispatcher, () =>
        {
            var window = createWindow();
            // Whatever the user does to close it counts as done; an app shutdown does not.
            window.Closed += (_, _) => { if (!_disposed) _model.Complete(); };
            return window;
        });
    }

    internal Window? CurrentWindow => _window.CurrentWindow;

    public void ShowIfNeeded()
    {
        if (!_model.IsCompleted) Show();
    }

    public void Show() => _window.Show();

    // A capture must not freeze the wizard into the screenshot, and using the shortcut means it did its job.
    public void Close() => _window.CurrentWindow?.Close();

    public void Dispose()
    {
        _disposed = true;
        _window.Dispose();
    }
}
