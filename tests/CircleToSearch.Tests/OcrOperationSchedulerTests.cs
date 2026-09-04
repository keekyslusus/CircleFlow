using CircleToSearch.TextRecognition;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class OcrOperationSchedulerTests
{
    [Fact]
    public async Task Six_operations_peak_at_two_concurrent_executions()
    {
        using var scheduler = new OcrOperationScheduler(2);
        var active = 0;
        var peak = 0;
        var operations = 0;

        var tasks = Enumerable.Range(0, 6).Select(_ => scheduler.RunAsync(async token =>
        {
            var current = Interlocked.Increment(ref active);
            Interlocked.Increment(ref operations);
            UpdateMaximum(ref peak, current);
            try
            {
                await Task.Delay(30, token);
                return current;
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }, CancellationToken.None)).ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(6, operations);
        Assert.Equal(2, peak);
    }

    [Fact]
    public async Task Cancellation_while_waiting_does_not_deadlock_or_run_the_operation()
    {
        using var scheduler = new OcrOperationScheduler(1);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = scheduler.RunAsync(async _ =>
        {
            entered.SetResult();
            await release.Task;
            return 1;
        }, CancellationToken.None);
        await entered.Task;
        using var cancellation = new CancellationTokenSource();
        var ran = false;
        var waiting = scheduler.RunAsync(_ =>
        {
            ran = true;
            return Task.FromResult(2);
        }, cancellation.Token);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        release.SetResult();

        Assert.Equal(1, await first);
        Assert.False(ran);
    }

    [Fact]
    public async Task Exception_releases_the_permit_for_the_next_operation()
    {
        using var scheduler = new OcrOperationScheduler(1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => scheduler.RunAsync<int>(
            _ => throw new InvalidOperationException("failure"), CancellationToken.None));
        var result = await scheduler.RunAsync(_ => Task.FromResult(42), CancellationToken.None);

        Assert.Equal(42, result);
    }

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        var current = Volatile.Read(ref maximum);
        while (candidate > current)
        {
            var observed = Interlocked.CompareExchange(ref maximum, candidate, current);
            if (observed == current) return;
            current = observed;
        }
    }
}
