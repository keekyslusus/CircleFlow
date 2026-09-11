namespace CircleToSearch;

internal sealed class PluginRuntimeLifetime
{
    private readonly Func<Task> _stopSession;
    private readonly Func<Task> _stopHotkey;
    private readonly Func<Task> _stopBrowser;
    private readonly Func<Task> _stopSigner;
    private readonly Func<Task> _stopRouter;
    private readonly IDisposable _musicHttpClient;
    private readonly IDisposable _musicThrottle;
    private readonly IDisposable _translationHttpClient;
    private readonly IDisposable _traceHttpClient;
    private readonly IDisposable? _translationMemory;
    private readonly PluginLog _log;
    private readonly object _gate = new();
    private Task? _stopTask;

    public PluginRuntimeLifetime(
        Func<Task> stopSession,
        Func<Task> stopHotkey,
        Func<Task> stopBrowser,
        Func<Task> stopSigner,
        Func<Task> stopRouter,
        IDisposable musicHttpClient,
        IDisposable musicThrottle,
        IDisposable translationHttpClient,
        IDisposable traceHttpClient,
        IDisposable? translationMemory,
        PluginLog log)
    {
        _stopSession = stopSession ?? throw new ArgumentNullException(nameof(stopSession));
        _stopHotkey = stopHotkey ?? throw new ArgumentNullException(nameof(stopHotkey));
        _stopBrowser = stopBrowser ?? throw new ArgumentNullException(nameof(stopBrowser));
        _stopSigner = stopSigner ?? throw new ArgumentNullException(nameof(stopSigner));
        _stopRouter = stopRouter ?? throw new ArgumentNullException(nameof(stopRouter));
        _musicHttpClient = musicHttpClient ?? throw new ArgumentNullException(nameof(musicHttpClient));
        _musicThrottle = musicThrottle ?? throw new ArgumentNullException(nameof(musicThrottle));
        _translationHttpClient = translationHttpClient ?? throw new ArgumentNullException(nameof(translationHttpClient));
        _traceHttpClient = traceHttpClient ?? throw new ArgumentNullException(nameof(traceHttpClient));
        _translationMemory = translationMemory;
        _log = log ?? throw new ArgumentNullException(nameof(log));
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
        var session = ObserveAsync(Start(_stopSession, "start-session-stop"), "stop-session");
        var hotkey = ObserveAsync(Start(_stopHotkey, "start-hotkey-stop"), "stop-hotkey");
        var browser = ObserveAsync(Start(_stopBrowser, "start-browser-stop"), "stop-browser");
        var signer = ObserveAsync(Start(_stopSigner, "start-signer-stop"), "stop-signer");

        var musicCleanup = DisposeAfterAsync(session,
            ("music-throttle", _musicThrottle),
            ("music-http", _musicHttpClient));
        var translationCleanup = DisposeAfterAsync(
            Task.WhenAll(session, signer),
            ("translation-http", _translationHttpClient));
        var router = StopRouterAfterSessionAsync(session);
        var traceCleanup = DisposeAfterAsync(router, ("trace-http", _traceHttpClient));
        var profilerCleanup = DisposeAfterAsync(
            Task.WhenAll(translationCleanup, signer),
            ("translation-profiler", _translationMemory));

        await Task.WhenAll(
            hotkey,
            browser,
            signer,
            musicCleanup,
            translationCleanup,
            traceCleanup,
            profilerCleanup).ConfigureAwait(false);
    }

    private Task Start(Func<Task> operation, string name)
    {
        try { return operation(); }
        catch (Exception exception)
        {
            _log.SafeError(nameof(PluginRuntimeLifetime), name, exception);
            return Task.CompletedTask;
        }
    }

    private async Task StopRouterAfterSessionAsync(Task session)
    {
        await session.ConfigureAwait(false);
        await ObserveAsync(Start(_stopRouter, "start-provider-router-stop"), "stop-provider-router")
            .ConfigureAwait(false);
    }

    private async Task ObserveAsync(Task task, string operation)
    {
        try { await task.ConfigureAwait(false); }
        catch (Exception exception)
        {
            _log.SafeError(nameof(PluginRuntimeLifetime), operation, exception);
        }
    }

    private async Task DisposeAfterAsync(Task dependency, params (string Name, IDisposable? Resource)[] resources)
    {
        await dependency.ConfigureAwait(false);
        foreach (var resource in resources)
        {
            if (resource.Resource is null) continue;
            try { resource.Resource.Dispose(); }
            catch (Exception exception)
            {
                _log.SafeError(nameof(PluginRuntimeLifetime), $"dispose-{resource.Name}", exception);
            }
        }
    }
}

internal sealed class PluginRuntimeStopAdapter : IDisposable
{
    private readonly Action _requestStop;
    private readonly Func<Task> _stop;
    private readonly PluginLog _log;
    private readonly TimeSpan _hostBudget;
    private readonly object _gate = new();
    private Task? _stopTask;
    private int _disposeWaitStarted;

    public PluginRuntimeStopAdapter(Action requestStop, Func<Task> stop, PluginLog log, TimeSpan hostBudget)
    {
        _requestStop = requestStop ?? throw new ArgumentNullException(nameof(requestStop));
        _stop = stop ?? throw new ArgumentNullException(nameof(stop));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        if (hostBudget < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(hostBudget));
        _hostBudget = hostBudget;
    }

    public Task StopAsync()
    {
        lock (_gate)
        {
            if (_stopTask is not null) return _stopTask;
            _requestStop();
            _stopTask = Task.Run(_stop);
            return _stopTask;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeWaitStarted, 1) != 0) return;
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        _log.Info(nameof(PluginRuntime), "disposing: canceling the active session and unregistering the hotkey");
        var stop = StopAsync();
        var remaining = _hostBudget - elapsed.Elapsed;
        try
        {
            if (remaining <= TimeSpan.Zero)
            {
                if (!stop.IsCompleted) throw new TimeoutException();
                stop.GetAwaiter().GetResult();
            }
            else
            {
                stop.WaitAsync(remaining).GetAwaiter().GetResult();
            }
        }
        catch (TimeoutException)
        {
            _log.Warn(nameof(PluginRuntime), "runtime cleanup exceeded the host shutdown budget and continues in the background");
            _ = stop.ContinueWith(
                task =>
                {
                    if (task.IsFaulted)
                    {
                        var aggregate = task.Exception!.Flatten();
                        var exception = aggregate.InnerExceptions.Count == 1
                            ? aggregate.InnerExceptions[0]
                            : aggregate;
                        _log.SafeError(nameof(PluginRuntime), "deferred-runtime-cleanup", exception);
                    }
                    else
                        _log.Info(nameof(PluginRuntime), "deferred runtime cleanup completed");
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        catch (Exception exception)
        {
            _log.SafeError(nameof(PluginRuntime), "runtime-cleanup", exception);
        }
    }
}
