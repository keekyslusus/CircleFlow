namespace CircleToSearch.Translation;

internal sealed class ScreenTranslationLifetime : IAsyncDisposable
{
    private readonly Func<Task> _stopSigner;
    private readonly IDisposable _httpClient;
    private readonly IDisposable? _profiler;
    private readonly PluginLog _log;
    private readonly object _gate = new();
    private Task? _stopTask;

    public ScreenTranslationLifetime(Func<Task> stopSigner, IDisposable httpClient,
        IDisposable? profiler, PluginLog log)
    {
        _stopSigner = stopSigner;
        _httpClient = httpClient;
        _profiler = profiler;
        _log = log;
    }

    public Task StopAsync(Task sessionSettled)
    {
        lock (_gate) return _stopTask ??= Task.Run(() => StopCoreAsync(sessionSettled));
    }

    public ValueTask DisposeAsync() => new(StopAsync(Task.CompletedTask));

    private async Task StopCoreAsync(Task sessionSettled)
    {
        var operations = new ShutdownOperations(_log, nameof(ScreenTranslationLifetime));
        var signer = operations.RunAsync("stop-signer", _stopSigner);
        var session = operations.RunAsync("wait-session", () => sessionSettled);
        await Task.WhenAll(session, signer).ConfigureAwait(false);
        await operations.DisposeAsync("translation-http", _httpClient).ConfigureAwait(false);
        await operations.DisposeAsync("translation-profiler", _profiler).ConfigureAwait(false);
        operations.ThrowIfFailed();
    }
}
