using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace CircleToSearch.Translation;

internal sealed class TranslationMemoryProfiler : IDisposable
{
    private readonly string _path;
    private readonly string _run = Guid.NewGuid().ToString("N");
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _gate = new();
    private readonly Dictionary<int, (string Kind, long Started)> _processes = [];
    private readonly Timer _timer;
    private DateTime _sampleUntil;
    private bool _disposed;
    private long _sequence;

    internal TranslationMemoryProfiler(string directory)
    {
        _path = Path.Combine(directory, "translation-memory.jsonl");
        _timer = new Timer(_ => Sample(), null, Timeout.Infinite, Timeout.Infinite);
    }

    internal void TrackProcesses(IEnumerable<(int Pid, string Kind)> processes)
    {
        lock (_gate)
        {
            if (_disposed) return;
            foreach (var (pid, kind) in processes)
            {
                try
                {
                    using var process = Process.GetProcessById(pid);
                    _processes[pid] = (kind, process.StartTime.ToUniversalTime().Ticks);
                }
                catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
    }

    internal void Mark(string stage, string? scope = null)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _sampleUntil = DateTime.UtcNow.AddMinutes(3);
            _timer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            Write(stage, scope);
        }
    }

    private void Sample()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (DateTime.UtcNow > _sampleUntil)
            {
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                return;
            }
            Write("sample", null);
        }
    }

    private void Write(string stage, string? scope)
    {
        try
        {
            var webview = _processes.Select(pair => ReadProcess(pair.Key, pair.Value.Kind, pair.Value.Started)).ToArray();
            var line = JsonSerializer.Serialize(new
            {
                schema = 1, run = _run, sequence = ++_sequence, utc = DateTime.UtcNow,
                elapsedMs = _clock.Elapsed.TotalMilliseconds, stage, scope,
                host = ReadProcess(Environment.ProcessId, "FlowLauncher", null), webview,
                managedLiveBytes = GC.GetTotalMemory(false),
                managedHeapBytesAtLastGc = GC.GetGCMemoryInfo().HeapSizeBytes,
                totalAllocatedBytes = GC.GetTotalAllocatedBytes(),
                gcCollections = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) }
            });
            if (File.Exists(_path) && new FileInfo(_path).Length > 5 * 1024 * 1024)
                File.Move(_path, _path + ".old", overwrite: true);
            File.AppendAllText(_path, line + Environment.NewLine);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    private static ProcessMemory ReadProcess(int pid, string kind, long? started)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (started is not null && process.StartTime.ToUniversalTime().Ticks != started)
                return new(pid, kind, "exited", null, null, null);
            return new(pid, kind, "running", process.PrivateMemorySize64, process.WorkingSet64,
                process.TotalProcessorTime.TotalMilliseconds);
        }
        catch (ArgumentException) { return new(pid, kind, "exited", null, null, null); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        { return new(pid, kind, "unavailable", null, null, null); }
    }

    private sealed record ProcessMemory(int Pid, string Kind, string State, long? PrivateBytes, long? WorkingSetBytes, double? CpuMs);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            Write("profiler_dispose", null);
            _disposed = true;
            _timer.Dispose();
        }
    }
}
