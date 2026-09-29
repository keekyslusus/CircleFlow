namespace CircleToSearch.MusicRecognition;

internal sealed class MusicRecognitionLifetime : IAsyncDisposable
{
    private readonly IDisposable _throttle;
    private readonly IDisposable _httpClient;
    private readonly PluginLog _log;
    private readonly object _gate = new();
    private Task? _stopTask;

    public MusicRecognitionLifetime(IDisposable throttle, IDisposable httpClient, PluginLog log)
    {
        _throttle = throttle;
        _httpClient = httpClient;
        _log = log;
    }

    public Task StopAsync()
    {
        lock (_gate) return _stopTask ??= Task.Run(StopCoreAsync);
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task StopCoreAsync()
    {
        var operations = new ShutdownOperations(_log, nameof(MusicRecognitionLifetime));
        await operations.DisposeAsync("music-throttle", _throttle).ConfigureAwait(false);
        await operations.DisposeAsync("music-http", _httpClient).ConfigureAwait(false);
        operations.ThrowIfFailed();
    }
}
