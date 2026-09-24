using System.Globalization;
using System.IO;

namespace CircleToSearch.Interop;

internal sealed class BrowserDataCleanup(AppPaths paths, PluginLog log, Func<DateTime>? utcNow = null)
{
    public const int Never = 0;
    public static IReadOnlyList<int> IntervalDays { get; } = [28, 56];
    private const string DataFolderName = "EBWebView";
    private const string TrashPrefix = DataFolderName + ".trash-";
    private readonly Func<DateTime> _utcNow = utcNow ?? (() => DateTime.UtcNow);

    private string StampPath => Path.Combine(paths.ProfilesDirectory, "last-browser-data-cleanup.txt");

    private IEnumerable<string> ProfileDirectories =>
        [paths.SearchProfileDirectory, paths.ImageTranslationProfileDirectory, paths.TraceVideoProfileDirectory];

    // Runs before any WebView2 environment exists; only renames here so a large profile never delays startup.
    public Task Run(int intervalDays)
    {
        try
        {
            if (intervalDays != Never && IsDue(intervalDays) && MoveDataToTrash()) WriteStamp();
            return DeleteTrashInBackground();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.SafeError(nameof(BrowserDataCleanup), "schedule", exception);
            return Task.CompletedTask;
        }
    }

    private bool IsDue(int intervalDays)
    {
        var now = _utcNow();
        if (ReadStamp() is not { } last || last > now)
        {
            // Start counting from the first run instead of wiping a profile the user just created.
            WriteStamp();
            return false;
        }
        return now - last >= TimeSpan.FromDays(intervalDays);
    }

    private bool MoveDataToTrash()
    {
        var moved = true;
        foreach (var profile in ProfileDirectories)
        {
            var data = Path.Combine(profile, DataFolderName);
            if (!Directory.Exists(data) || !paths.IsInsideData(data)) continue;
            try { Directory.Move(data, Path.Combine(profile, TrashPrefix + Guid.NewGuid().ToString("N"))); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A browser process left over from a previous run still holds the profile; retry on the next start.
                log.Warn(nameof(BrowserDataCleanup), $"browser data is in use and was kept: {exception.Message}");
                moved = false;
            }
        }
        if (moved) log.Info(nameof(BrowserDataCleanup), "browser data moved to trash");
        return moved;
    }

    private Task DeleteTrashInBackground()
    {
        var trash = ProfileDirectories
            .Where(Directory.Exists)
            .SelectMany(profile => Directory.EnumerateDirectories(profile, TrashPrefix + "*"))
            .Where(paths.IsInsideData)
            .ToArray();
        if (trash.Length == 0) return Task.CompletedTask;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // An interrupted deletion is finished by the next start, so the thread need not keep the process alive.
        new Thread(() =>
        {
            foreach (var directory in trash) Delete(directory);
            completion.SetResult();
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "CircleFlow browser data cleanup",
        }.Start();
        return completion.Task;
    }

    private void Delete(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        // An exception escaping this thread would end the process; whatever is left is retried on the next start.
        catch (Exception exception)
        {
            log.SafeError(nameof(BrowserDataCleanup), "delete", exception);
        }
    }

    private DateTime? ReadStamp()
    {
        if (!File.Exists(StampPath)) return null;
        return DateTime.TryParse(File.ReadAllText(StampPath).Trim(), CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var stamp) && stamp.Kind == DateTimeKind.Utc
            ? stamp
            : null;
    }

    private void WriteStamp()
    {
        Directory.CreateDirectory(paths.ProfilesDirectory);
        File.WriteAllText(StampPath, _utcNow().ToString("O", CultureInfo.InvariantCulture));
    }
}
