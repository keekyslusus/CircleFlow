using CircleToSearch.Interop;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class StaDispatcherTests
{
    [Fact]
    public async Task Stop_is_idempotent_and_rejects_late_posts()
    {
        var dispatcher = new StaDispatcher("STA dispatcher lifecycle test");

        var first = dispatcher.StopAsync();
        var second = dispatcher.StopAsync();

        Assert.Same(first, second);
        await first.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(dispatcher.TryPost(() => { }));
        dispatcher.Dispose();
    }

    [Fact]
    public async Task Stop_requested_on_the_STA_does_not_wait_for_its_own_thread()
    {
        var dispatcher = new StaDispatcher("STA dispatcher self stop test");
        var requested = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(dispatcher.TryPost(() => requested.TrySetResult(dispatcher.StopAsync())));

        var stop = await requested.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await stop.WaitAsync(TimeSpan.FromSeconds(2));
        dispatcher.Dispose();
    }

    [Fact]
    public async Task Initialization_timeout_stops_a_dispatcher_that_crossed_the_ready_boundary()
    {
        using var beforeRun = new ManualResetEventSlim();
        using var allowRun = new ManualResetEventSlim();
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Assert.Throws<TimeoutException>(() => new StaDispatcher(
            "STA dispatcher initialization race test",
            TimeSpan.FromSeconds(2),
            (ready, timeout) =>
            {
                Assert.True(ready.Wait(timeout));
                Assert.True(beforeRun.Wait(timeout));
                return false;
            },
            () =>
            {
                beforeRun.Set();
                allowRun.Wait();
            },
            () => stopped.TrySetResult()));

        allowRun.Set();
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }
}
