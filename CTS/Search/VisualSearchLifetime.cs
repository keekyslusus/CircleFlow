namespace CircleToSearch.Search;

internal sealed class VisualSearchLifetime : IAsyncDisposable
{
    private readonly Func<Task> _stopRouter;
    private readonly IDisposable _traceHttpClient;
    private readonly PluginLog _log;
    private readonly object _gate = new();
    private Task? _stopTask;

    public VisualSearchLifetime(Func<Task> stopRouter, IDisposable traceHttpClient, PluginLog log)
    {
        _stopRouter = stopRouter;
        _traceHttpClient = traceHttpClient;
        _log = log;
    }

    public Task StopAsync()
    {
        lock (_gate) return _stopTask ??= Task.Run(StopCoreAsync);
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task StopCoreAsync()
    {
        var operations = new ShutdownOperations(_log, nameof(VisualSearchLifetime));
        await operations.RunAsync("stop-router", _stopRouter).ConfigureAwait(false);
        await operations.DisposeAsync("trace-http", _traceHttpClient).ConfigureAwait(false);
        operations.ThrowIfFailed();
    }
}
