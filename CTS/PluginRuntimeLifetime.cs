namespace CircleToSearch;

internal sealed class PluginRuntimeLifetime
{
    private readonly Func<Task> _stopSession;
    private readonly Func<Task> _stopHotkey;
    private readonly Func<Task> _stopBrowser;
    private readonly Func<Task> _stopMusic;
    private readonly Func<Task, Task> _stopTranslation;
    private readonly Func<Task> _stopVisualSearch;
    private readonly PluginLog _log;
    private readonly object _gate = new();
    private Task? _stopTask;

    public PluginRuntimeLifetime(
        Func<Task> stopSession,
        Func<Task> stopHotkey,
        Func<Task> stopBrowser,
        Func<Task> stopMusic,
        Func<Task, Task> stopTranslation,
        Func<Task> stopVisualSearch,
        PluginLog log)
    {
        _stopSession = stopSession;
        _stopHotkey = stopHotkey;
        _stopBrowser = stopBrowser;
        _stopMusic = stopMusic;
        _stopTranslation = stopTranslation;
        _stopVisualSearch = stopVisualSearch;
        _log = log;
    }

    public Task StopAsync()
    {
        TaskCompletionSource completion;
        lock (_gate)
        {
            if (_stopTask is not null) return _stopTask;
            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _stopTask = completion.Task;
        }
        _ = CompleteStopAsync(completion);
        return completion.Task;
    }

    private async Task CompleteStopAsync(TaskCompletionSource completion)
    {
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (Exception exception) { completion.TrySetException(exception); }
    }

    private async Task StopCoreAsync()
    {
        var operations = new ShutdownOperations(_log, nameof(PluginRuntimeLifetime));
        var session = operations.RunAsync("stop-session", _stopSession);
        var hotkey = operations.RunAsync("stop-hotkey", _stopHotkey);
        var browser = operations.RunAsync("stop-browser", _stopBrowser);
        var translation = operations.RunAsync("stop-translation", () => _stopTranslation(session));
        var music = StopAfterSessionAsync(session, "stop-music", _stopMusic, operations);
        var visualSearch = StopAfterSessionAsync(session, "stop-visual-search", _stopVisualSearch, operations);

        await Task.WhenAll(hotkey, browser, translation, music, visualSearch).ConfigureAwait(false);
        operations.ThrowIfFailed();
    }

    private static async Task StopAfterSessionAsync(Task session, string name, Func<Task> stop,
        ShutdownOperations operations)
    {
        await session.ConfigureAwait(false);
        await operations.RunAsync(name, stop).ConfigureAwait(false);
    }
}
