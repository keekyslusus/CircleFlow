using System.IO;
using System.Text.Json;
using CircleToSearch.MusicRecognition.Shazam;

namespace CircleToSearch.MusicRecognition;

internal sealed record MusicHistoryEntry(ShazamRecognition Track, DateTimeOffset RecognizedAt);

internal sealed class MusicHistory(
    string filePath,
    Func<bool> saving,
    Func<int> retentionDays,
    TimeProvider time,
    PluginLog log)
{
    public const int KeepForever = 0;
    // The settings page lists every entry without virtualization.
    public const int MaxEntries = 200;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly Lock _gate = new();
    private IReadOnlyList<MusicHistoryEntry>? _entries;

    public static IReadOnlyList<int> RetentionDays { get; } = [7, 28, 90];

    public static bool IsValidRetention(int days) => days == KeepForever || RetentionDays.Contains(days);

    // Raised on the thread that changed the history.
    public event Action? Changed;

    public TimeProvider Time => time;

    // Newest first.
    public IReadOnlyList<MusicHistoryEntry> Entries
    {
        get
        {
            lock (_gate) return (Loaded() ?? []).Where(IsKept).ToArray();
        }
    }

    public void Record(ShazamRecognition track)
    {
        if (!saving()) return;
        Update(entries => [new MusicHistoryEntry(track, time.GetUtcNow()), .. entries]);
    }

    public bool Clear() => Update(_ => []);

    public bool ApplyRetention() => Update(entries => entries);

    private bool Update(Func<IReadOnlyList<MusicHistoryEntry>, IReadOnlyList<MusicHistoryEntry>> change)
    {
        lock (_gate)
        {
            // Writing over a file that could not be read would lose the history in it.
            if (Loaded() is not { } current) return false;
            var updated = change(current).Where(IsKept).Take(MaxEntries).ToArray();
            if (updated.SequenceEqual(current)) return true;
            try { Write(updated); }
            catch (Exception exception)
            {
                log.SafeError(nameof(MusicHistory), "save-history", exception);
                return false;
            }
            _entries = updated;
        }
        Changed?.Invoke();
        return true;
    }

    private bool IsKept(MusicHistoryEntry entry)
    {
        var days = retentionDays();
        return days == KeepForever || entry.RecognizedAt > time.GetUtcNow().AddDays(-days);
    }

    private IReadOnlyList<MusicHistoryEntry>? Loaded()
    {
        if (_entries is not null) return _entries;
        try
        {
            if (!File.Exists(filePath)) return _entries = [];
            using var stream = File.OpenRead(filePath);
            return _entries = (JsonSerializer.Deserialize<MusicHistoryEntry?[]>(stream) ?? [])
                .OfType<MusicHistoryEntry>()
                .Where(entry => entry.Track is { Title.Length: > 0, Artist: not null })
                .OrderByDescending(entry => entry.RecognizedAt)
                .ToArray();
        }
        catch (JsonException exception)
        {
            log.SafeError(nameof(MusicHistory), "read-history", exception);
            return _entries = [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Not cached: the file may be readable on the next attempt.
            log.SafeError(nameof(MusicHistory), "load-history", exception);
            return null;
        }
    }

    private void Write(IReadOnlyList<MusicHistoryEntry> entries)
    {
        var temporary = filePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, entries, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, filePath, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
