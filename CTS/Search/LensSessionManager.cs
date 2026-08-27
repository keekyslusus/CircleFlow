using System.IO;

namespace CircleToSearch.Search;

// Owns the cached browser-minted session: cached and fresh → reuse; missing or stale → farm a
// new one through the WebView2 engine. Farms are serialized so parallel searches cannot race.
public sealed class LensSessionManager
{
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(18);

    private readonly LensSessionStore _store;
    private readonly Func<Task<LensSession?>> _farm;
    private readonly PluginLog _log;
    private readonly SemaphoreSlim _farmingGate = new(1, 1);

    public LensSessionManager(LensSessionStore store, Func<Task<LensSession?>> farm, PluginLog log)
    {
        _store = store;
        _farm = farm;
        _log = log;
    }

    public LensSession? GetCached()
    {
        var session = _store.Load();
        if (session is null || session.Cookies.Count == 0) return null;
        return DateTimeOffset.UtcNow - session.IssuedAt < SessionLifetime ? session : null;
    }

    public async Task<LensSession?> GetOrFarmAsync(bool force)
    {
        if (!force)
        {
            var cached = GetCached();
            if (cached is not null) return cached;
        }

        await _farmingGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!force)
            {
                var cached = GetCached();
                if (cached is not null) return cached;
            }

            _log.Info(nameof(LensSessionManager), "farming a fresh Lens session via the embedded browser engine");
            var session = await _farm().ConfigureAwait(false);
            if (session is null || session.Cookies.Count == 0)
            {
                _log.Warn(nameof(LensSessionManager), "session farming returned no cookies");
                return null;
            }
            _store.Save(session);
            return session;
        }
        catch (Exception exception)
        {
            _log.Error(nameof(LensSessionManager), "session farming failed", exception);
            return null;
        }
        finally
        {
            _farmingGate.Release();
        }
    }

    public void Invalidate() => _store.Delete();
}
