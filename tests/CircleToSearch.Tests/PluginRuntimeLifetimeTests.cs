using Xunit;

namespace CircleToSearch.Tests;

public sealed class PluginRuntimeLifetimeTests
{
    [Fact]
    public async Task Independent_stops_begin_immediately_and_shared_resources_wait_for_their_users()
    {
        var events = new List<string>();
        var session = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lifetime = Create(
            events,
            stopSession: () =>
            {
                lock (events) events.Add("session-stop");
                return session.Task;
            });

        var first = lifetime.StopAsync();
        var second = lifetime.StopAsync();

        Assert.Same(first, second);
        Assert.Contains("hotkey-stop", events);
        Assert.Contains("browser-stop", events);
        Assert.Contains("signer-stop", events);
        Assert.DoesNotContain("router-stop", events);
        Assert.DoesNotContain("music-http", events);

        session.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(events.IndexOf("router-stop") < events.IndexOf("trace-http"));
        Assert.True(events.IndexOf("session-stop") < events.IndexOf("music-throttle"));
        Assert.True(events.IndexOf("signer-stop") < events.IndexOf("translation-http"));
        Assert.Equal(1, events.Count(value => value == "music-http"));
        Assert.Equal(1, events.Count(value => value == "translation-profiler"));
    }

    [Fact]
    public async Task Faulting_branches_and_resources_do_not_skip_independent_cleanup()
    {
        var events = new List<string>();
        var lifetime = Create(
            events,
            stopHotkey: () => throw new InvalidOperationException("hotkey secret"),
            throwingResource: "translation-http");

        await lifetime.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Contains("browser-stop", events);
        Assert.Contains("signer-stop", events);
        Assert.Contains("router-stop", events);
        Assert.Contains("trace-http", events);
        Assert.Contains("translation-profiler", events);
    }

    private static PluginRuntimeLifetime Create(
        List<string> events,
        Func<Task>? stopSession = null,
        Func<Task>? stopHotkey = null,
        string? throwingResource = null)
    {
        Task Stop(string name)
        {
            lock (events) events.Add(name);
            return Task.CompletedTask;
        }

        return new PluginRuntimeLifetime(
            stopSession ?? (() => Stop("session-stop")),
            stopHotkey ?? (() => Stop("hotkey-stop")),
            () => Stop("browser-stop"),
            () => Stop("signer-stop"),
            () => Stop("router-stop"),
            new Resource("music-http", events, throwingResource),
            new Resource("music-throttle", events, throwingResource),
            new Resource("translation-http", events, throwingResource),
            new Resource("trace-http", events, throwingResource),
            new Resource("translation-profiler", events, throwingResource),
            NewLog());
    }

    private static PluginLog NewLog()
    {
        var directory = Path.Combine(TestOutputPaths.TempDirectory, "runtime-lifetime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new PluginLog(directory);
    }

    private sealed class Resource(string name, List<string> events, string? throwingResource) : IDisposable
    {
        public void Dispose()
        {
            lock (events) events.Add(name);
            if (name == throwingResource) throw new InvalidOperationException("resource secret");
        }
    }
}

public sealed class PluginRuntimeStopAdapterTests
{
    [Fact]
    public async Task Dispose_spends_its_budget_once_and_cleanup_remains_observable()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var requestCalls = 0;
        var adapter = new PluginRuntimeStopAdapter(
            () => Interlocked.Increment(ref requestCalls),
            () =>
            {
                Interlocked.Increment(ref calls);
                return completion.Task;
            },
            NewLog(out _),
            TimeSpan.Zero);

        adapter.Dispose();
        adapter.Dispose();

        Assert.Equal(1, requestCalls);
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref calls) == 1, TimeSpan.FromSeconds(2)));
        Assert.Equal(1, calls);
        Assert.False(adapter.StopAsync().IsCompleted);
        completion.SetResult();
        await adapter.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Deferred_fault_is_observed_without_logging_its_message()
    {
        const string secret = "cleanup payload 5917";
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var log = NewLog(out var directory);
        var adapter = new PluginRuntimeStopAdapter(() => { }, () => completion.Task, log, TimeSpan.Zero);
        adapter.Dispose();

        completion.SetException(new InvalidOperationException(secret));
        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.StopAsync());
        Assert.True(SpinWait.SpinUntil(
            () => LogContains(Path.Combine(directory, "plugin.log"), "deferred-runtime-cleanup"),
            TimeSpan.FromSeconds(2)));
        var contents = File.ReadAllText(Path.Combine(directory, "plugin.log"));
        Assert.DoesNotContain(secret, contents);
        Assert.Contains(typeof(InvalidOperationException).FullName!, contents);
    }

    [Fact]
    public async Task Dispose_budget_includes_a_blocking_synchronous_stop_callback()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var requestCalls = 0;
        var adapter = new PluginRuntimeStopAdapter(
            () => Interlocked.Increment(ref requestCalls),
            () =>
            {
                entered.Set();
                release.Wait();
                return Task.CompletedTask;
            },
            NewLog(out _),
            TimeSpan.FromMilliseconds(20));
        var disposeThread = new Thread(adapter.Dispose) { IsBackground = true };
        disposeThread.Start();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));

        Assert.True(disposeThread.Join(TimeSpan.FromSeconds(1)));
        Assert.Equal(1, requestCalls);
        Assert.False(adapter.StopAsync().IsCompleted);

        release.Set();
        await adapter.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static bool LogContains(string path, string value)
    {
        try { return File.Exists(path) && File.ReadAllText(path).Contains(value); }
        catch (IOException) { return false; }
    }

    private static PluginLog NewLog(out string directory)
    {
        directory = Path.Combine(TestOutputPaths.TempDirectory, "runtime-adapter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new PluginLog(directory);
    }
}
