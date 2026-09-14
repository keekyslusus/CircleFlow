using System.Windows;
using System.Windows.Threading;

namespace CircleToSearch.Shell;

internal sealed class AppLifetime(Application application, PluginLog log, ShutdownWatchdog watchdog)
{
    private readonly List<(string Name, Action Stop)> _stop = [];
    private readonly List<(string Name, Func<Task> Cleanup)> _cleanup = [];
    private readonly List<(string Name, Action Release)> _release = [];
    private readonly CancellationTokenSource _startupCancellation = new();
    private readonly TaskCompletionSource _startupFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _exitTask;
    private int _exitCode;
    private bool _sessionEnding;
    internal Exception? StartupFailure { get; private set; }

    public void AddStop(string name, Action stop)
    {
        application.Dispatcher.VerifyAccess();
        if (_exitTask is not null) Try(name, stop);
        else _stop.Add((name, stop));
    }

    public void AddCleanup(string name, Func<Task> cleanup)
    {
        application.Dispatcher.VerifyAccess();
        _cleanup.Add((name, cleanup));
    }

    public void AddRelease(string name, Action release)
    {
        application.Dispatcher.VerifyAccess();
        _release.Add((name, release));
    }

    public int Run(Action<CancellationToken>? startup = null, CancellationToken cancellation = default)
    {
        void Start(object sender, StartupEventArgs args)
        {
            try
            {
                _startupCancellation.Token.ThrowIfCancellationRequested();
                startup?.Invoke(_startupCancellation.Token);
            }
            catch (OperationCanceledException) when (_startupCancellation.IsCancellationRequested) { }
            catch (Exception exception)
            {
                StartupFailure = exception;
                _exitCode = 1;
                log.SafeError(nameof(AppLifetime), "startup", exception);
            }
            finally { _startupFinished.TrySetResult(); }
            if (StartupFailure is not null || _startupCancellation.IsCancellationRequested) _ = RequestExitAsync();
        }
        application.Startup += Start;
        application.SessionEnding += OnSessionEnding;
        var registration = cancellation.Register(() =>
        {
            watchdog.Start();
            try { _startupCancellation.Cancel(); }
            finally { application.Dispatcher.BeginInvoke(new Action(() => _ = RequestExitAsync())); }
        });
        try { return application.Run(); }
        finally
        {
            application.Startup -= Start;
            application.SessionEnding -= OnSessionEnding;
            registration.Dispose();
            _startupCancellation.Dispose();
            watchdog.Dispose();
        }
    }

    public Task RequestExitAsync()
    {
        application.Dispatcher.VerifyAccess();
        if (_exitTask is not null) return _exitTask;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _exitTask = completion.Task;
        watchdog.Start();
        Try("cancel-startup", _startupCancellation.Cancel);
        foreach (var entry in _stop) Try(entry.Name, entry.Stop);
        _ = ExitAsync(completion);
        return _exitTask;
    }

    private async Task ExitAsync(TaskCompletionSource completion)
    {
        await _startupFinished.Task;
        await Task.WhenAll(_cleanup.Select(entry => CleanupAsync(entry.Name, entry.Cleanup)));
        foreach (var entry in _release) Try(entry.Name, entry.Release);
        try
        {
            if (!_sessionEnding) application.Shutdown(_exitCode);
        }
        finally
        {
            completion.TrySetResult();
        }
    }

    private async Task CleanupAsync(string name, Func<Task> cleanup)
    {
        try { await cleanup(); }
        catch (Exception exception) { Failure(name, exception); }
    }

    private void Try(string name, Action action)
    {
        try { action(); }
        catch (Exception exception) { Failure(name, exception); }
    }

    private void Failure(string name, Exception exception)
    {
        _exitCode = 1;
        log.SafeError(nameof(AppLifetime), name, exception);
    }

    private void OnSessionEnding(object sender, SessionEndingCancelEventArgs args)
    {
        if (_sessionEnding) return;
        _sessionEnding = true;
        var frame = new DispatcherFrame();
        var deadline = new DispatcherTimer(DispatcherPriority.Send, application.Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(2),
        };
        deadline.Tick += (_, _) => frame.Continue = false;
        deadline.Start();
        try
        {
            var exit = RequestExitAsync();
            if (!exit.IsCompleted)
            {
                _ = EndFrameAsync(exit, frame);
                // WPF shuts down after this handler returns. Pump cleanup continuations here,
                // without vetoing the Windows session or waiting indefinitely for async work.
                Dispatcher.PushFrame(frame);
            }
            if (!exit.IsCompleted)
            {
                _exitCode = 1;
            }
            application.Shutdown(_exitCode);
        }
        finally { deadline.Stop(); }
    }

    private static async Task EndFrameAsync(Task exit, DispatcherFrame frame)
    {
        await exit;
        frame.Continue = false;
    }
}
