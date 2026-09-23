using System.Text.Json;
using CircleToSearch.Translation;
using Xunit;

namespace CircleToSearch.Tests;

[Trait("Category", "Slow")]
public sealed class TranslationMemoryProfilerTests
{
    [Fact]
    public void Marks_include_real_host_counters_and_dispose_stops_writes()
    {
        var directory = TestOutputPaths.NewTempDirectory(nameof(Marks_include_real_host_counters_and_dispose_stops_writes));
        var path = Path.Combine(directory, "translation-memory.jsonl");
        using var profiler = new TranslationMemoryProfiler(directory);
        profiler.Mark("overlay_open", "session-a");
        profiler.Mark("original_shown", "session-a");
        profiler.Dispose();
        var lines = File.ReadAllLines(path);
        Assert.Equal(3, lines.Length);
        using var first = JsonDocument.Parse(lines[0]);
        Assert.Equal("overlay_open", first.RootElement.GetProperty("stage").GetString());
        Assert.Equal("session-a", first.RootElement.GetProperty("scope").GetString());
        var host = first.RootElement.GetProperty("host");
        Assert.Equal("CircleFlow", host.GetProperty("Kind").GetString());
        Assert.Equal(Environment.ProcessId, host.GetProperty("Pid").GetInt32());
        Assert.True(host.GetProperty("PrivateBytes").GetInt64() > 0);
        Assert.True(host.GetProperty("WorkingSetBytes").GetInt64() > 0);
        Assert.Empty(first.RootElement.GetProperty("webview").EnumerateArray());
        profiler.Mark("should_not_be_written");
        Assert.Equal(lines, File.ReadAllLines(path));
    }

    [Fact]
    public async Task Periodic_samples_continue_after_an_action_and_include_tracked_processes()
    {
        var directory = TestOutputPaths.NewTempDirectory(nameof(Periodic_samples_continue_after_an_action_and_include_tracked_processes));
        var path = Path.Combine(directory, "translation-memory.jsonl");
        using var profiler = new TranslationMemoryProfiler(directory);
        profiler.TrackProcesses([(Environment.ProcessId, "test-worker")]);
        profiler.Mark("webview_closed");
        for (var attempt = 0; attempt < 40 && File.ReadAllLines(path).Length < 2; attempt++)
            await Task.Delay(100);
        profiler.Dispose();
        var lines = File.ReadAllLines(path);
        Assert.Contains(lines, line => line.Contains("\"stage\":\"sample\""));
        using var first = JsonDocument.Parse(lines[0]);
        var worker = Assert.Single(first.RootElement.GetProperty("webview").EnumerateArray());
        Assert.Equal("test-worker", worker.GetProperty("Kind").GetString());
        Assert.Equal("running", worker.GetProperty("State").GetString());
    }
}
