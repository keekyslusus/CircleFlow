using Xunit;

namespace CircleToSearch.Tests;

public sealed class PluginRuntimeLifetimeTests
{
    [Fact]
    public async Task Synchronously_blocked_stop_does_not_prevent_independent_branches()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var events = new List<string>();
        var lifetime = Create(events, stopHotkey: () =>
        {
            entered.Set();
            release.Wait();
            return Task.CompletedTask;
        });
        var stopping = lifetime.StopAsync();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            Assert.True(SpinWait.SpinUntil(() =>
            {
                lock (events) return events.Contains("trace-http") && events.Contains("translation-profiler");
            }, TimeSpan.FromSeconds(2)));
            Assert.False(stopping.IsCompleted);
        }
        finally { release.Set(); }
        await stopping.WaitAsync(TimeSpan.FromSeconds(2));
    }

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
        Assert.True(SpinWait.SpinUntil(() => { lock (events) return events.Contains("hotkey-stop") && events.Contains("browser-stop") && events.Contains("signer-stop"); }, TimeSpan.FromSeconds(2)));
        lock (events)
        {
            Assert.DoesNotContain("router-stop", events);
            Assert.DoesNotContain("music-http", events);
        }

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

        await Assert.ThrowsAsync<AggregateException>(() => lifetime.StopAsync().WaitAsync(TimeSpan.FromSeconds(2)));

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
