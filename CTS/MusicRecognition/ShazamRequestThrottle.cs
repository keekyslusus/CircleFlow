namespace CircleToSearch.MusicRecognition;

public interface IMusicRecognitionClock
{
    TimeSpan Elapsed { get; }
    DateTimeOffset UtcNow { get; }
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public sealed class SystemMusicRecognitionClock : IMusicRecognitionClock
{
    private readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();

    public TimeSpan Elapsed => _stopwatch.Elapsed;
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}

public sealed class ShazamRequestThrottle : IDisposable
{
    public static readonly TimeSpan MinimumRequestInterval = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan FallbackCooldown = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _request = new(1, 1);
    private readonly object _sync = new();
    private readonly IMusicRecognitionClock _clock;
    private TimeSpan? _lastRequestStart;
    private DateTimeOffset _cooldownUntil;

    public ShazamRequestThrottle(IMusicRecognitionClock clock) => _clock = clock;

    public bool TryGetCooldownRemaining(out TimeSpan remaining)
    {
        lock (_sync)
        {
            remaining = _cooldownUntil - _clock.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
                return false;
            }
            return true;
        }
    }

    public void StartCooldown(TimeSpan? retryAfter)
    {
        var duration = retryAfter is { } valid && valid > TimeSpan.Zero ? valid : FallbackCooldown;
        lock (_sync)
        {
            var candidate = _clock.UtcNow + duration;
            if (candidate > _cooldownUntil) _cooldownUntil = candidate;
        }
    }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> request, CancellationToken cancellationToken)
    {
        await _request.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TimeSpan delay;
            lock (_sync)
            {
                delay = _lastRequestStart is null
                    ? TimeSpan.Zero
                    : MinimumRequestInterval - (_clock.Elapsed - _lastRequestStart.Value);
            }
            if (delay > TimeSpan.Zero)
                await _clock.DelayAsync(delay, cancellationToken).ConfigureAwait(false);
            lock (_sync) _lastRequestStart = _clock.Elapsed;
            return await request(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _request.Release();
        }
    }

    public void Dispose() => _request.Dispose();
}
