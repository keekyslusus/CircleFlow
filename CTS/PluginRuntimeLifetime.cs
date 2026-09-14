using System.Collections.Concurrent;

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
    private readonly ConcurrentQueue<Exception> _failures = new();

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
        if (!_failures.IsEmpty) throw new AggregateException(_failures);
    }

    private Task Start(Func<Task> operation, string name)
    {
        try { return Task.Run(operation); }
        catch (Exception exception)
        {
            _failures.Enqueue(exception);
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
            _failures.Enqueue(exception);
            _log.SafeError(nameof(PluginRuntimeLifetime), operation, exception);
        }
    }

    private async Task DisposeAfterAsync(Task dependency, params (string Name, IDisposable? Resource)[] resources)
    {
        await dependency.ConfigureAwait(false);
        foreach (var resource in resources)
        {
            if (resource.Resource is null) continue;
            try { await Task.Run(resource.Resource.Dispose).ConfigureAwait(false); }
            catch (Exception exception)
            {
                _failures.Enqueue(exception);
                _log.SafeError(nameof(PluginRuntimeLifetime), $"dispose-{resource.Name}", exception);
            }
        }
    }
}
