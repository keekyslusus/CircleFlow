using Xunit;

namespace CircleToSearch.Tests;

public sealed class PluginRuntimeLifetimeTests
{
    [Fact]
    public async Task Starts_independent_branches_and_passes_a_settled_session_barrier()
    {
        var session = Gate();
        var translation = Gate();
        var hotkey = Gate();
        var browser = Gate();
        var started = new List<string>();
        Task? receivedBarrier = null;
        var lifetime = new PluginRuntimeLifetime(
            () => { Add("session"); return session.Task; },
            () => { Add("hotkey"); return hotkey.Task; },
            () => { Add("browser"); return browser.Task; },
            () => { Add("music"); return Task.CompletedTask; },
            barrier => { receivedBarrier = barrier; Add("translation"); return translation.Task; },
            () => { Add("visual"); return Task.CompletedTask; }, NewLog());

        void Add(string name) { lock (started) started.Add(name); }
        bool Has(string name) { lock (started) return started.Contains(name); }

        var first = lifetime.StopAsync();
        Assert.Same(first, lifetime.StopAsync());
        Assert.True(SpinWait.SpinUntil(() => Has("session") && Has("hotkey") && Has("browser") && Has("translation"),
            TimeSpan.FromSeconds(3)));
        Assert.NotNull(receivedBarrier);
        Assert.False(receivedBarrier.IsCompleted);
        Assert.False(Has("music"));
        Assert.False(Has("visual"));
        translation.SetResult();
        Assert.False(first.IsCompleted);
        session.SetResult();
        await receivedBarrier.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(SpinWait.SpinUntil(() => Has("music") && Has("visual"), TimeSpan.FromSeconds(3)));
        Assert.False(first.IsCompleted);
        hotkey.SetResult();
        browser.SetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(3));
        lock (started) Assert.Equal(1, started.Count(name => name == "music"));
    }

    [Fact]
    public async Task Blocked_callback_does_not_prevent_other_branches()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var other = Gate();
        var lifetime = new PluginRuntimeLifetime(
            () => Task.CompletedTask,
            () => { entered.Set(); release.Wait(); return Task.CompletedTask; },
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            _ => { other.SetResult(); return Task.CompletedTask; },
            () => Task.CompletedTask, NewLog());
        var stopping = lifetime.StopAsync();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            await other.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(stopping.IsCompleted);
        }
        finally { release.Set(); }
        await stopping.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Blocked_music_stop_does_not_delay_visual_search()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var visual = Gate();
        var lifetime = new PluginRuntimeLifetime(
            () => Task.CompletedTask, () => Task.CompletedTask, () => Task.CompletedTask,
            () => { entered.Set(); release.Wait(); return Task.CompletedTask; },
            _ => Task.CompletedTask,
            () => { visual.SetResult(); return Task.CompletedTask; }, NewLog());
        var stopping = lifetime.StopAsync();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
            await visual.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(stopping.IsCompleted);
        }
        finally { release.Set(); }
        await stopping.WaitAsync(TimeSpan.FromSeconds(3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_or_cancelled_session_releases_dependents_and_preserves_error(bool cancel)
    {
        var session = Gate();
        var music = Gate();
        var visual = Gate();
        var lifetime = new PluginRuntimeLifetime(
            () => session.Task, () => Task.CompletedTask, () => Task.CompletedTask,
            () => { music.SetResult(); return Task.CompletedTask; },
            _ => Task.CompletedTask,
            () => { visual.SetResult(); return Task.CompletedTask; }, NewLog());
        var stopping = lifetime.StopAsync();
        if (cancel) session.SetCanceled();
        else session.SetException(new InvalidOperationException("session"));
        await Task.WhenAll(music.Task, visual.Task).WaitAsync(TimeSpan.FromSeconds(3));
        var error = await Assert.ThrowsAsync<AggregateException>(() => stopping.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Single(error.InnerExceptions);
    }

    [Fact]
    public async Task Multiple_branch_failures_are_flattened_once()
    {
        var first = new InvalidOperationException("first");
        var second = new ArgumentException("second");
        var third = new ApplicationException("third");
        var fourth = new NotSupportedException("fourth");
        var lifetime = new PluginRuntimeLifetime(
            () => Task.FromException(first),
            () => Task.FromException(second),
            () => Task.CompletedTask,
            () => Task.FromException(new AggregateException(third, fourth)),
            _ => Task.CompletedTask,
            () => Task.CompletedTask, NewLog());
        var error = await Assert.ThrowsAsync<AggregateException>(() => lifetime.StopAsync().WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(4, error.Flatten().InnerExceptions.Count);
        Assert.Contains(first, error.InnerExceptions);
        Assert.Contains(second, error.InnerExceptions);
        Assert.Contains(third, error.InnerExceptions);
        Assert.Contains(fourth, error.InnerExceptions);
    }

    [Fact]
    public async Task Concurrent_calls_share_one_stop_task()
    {
        var session = Gate();
        var calls = 0;
        var lifetime = new PluginRuntimeLifetime(
            () => { Interlocked.Increment(ref calls); return session.Task; },
            () => Task.CompletedTask, () => Task.CompletedTask,
            () => Task.CompletedTask, _ => Task.CompletedTask,
            () => Task.CompletedTask, NewLog());
        var results = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => Task.Factory.StartNew(lifetime.StopAsync, CancellationToken.None,
                TaskCreationOptions.None, TaskScheduler.Default)));
        Assert.All(results, task => Assert.Same(results[0], task));
        session.SetResult();
        await results[0].WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, calls);
    }

    internal static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static PluginLog NewLog()
    {
        var directory = Path.Combine(TestOutputPaths.TempDirectory, "runtime-lifetime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new PluginLog(directory);
    }
}
