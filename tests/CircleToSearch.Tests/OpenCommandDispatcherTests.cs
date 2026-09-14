using System.Windows.Threading;
using CircleToSearch.Shell;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class OpenCommandDispatcherTests
{
    [Fact]
    public void Readiness_without_activation_does_not_open_anything() => OnSta(() =>
    {
        using var activation = Create();
        var opens = 0;
        activation.SetReady(() => { opens++; return Task.CompletedTask; });
        Pump();
        Assert.Equal(0, opens);
    });

    [Fact]
    public void Concurrent_startup_requests_coalesce_and_run_on_the_owner_dispatcher() => OnSta(() =>
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        using var activation = Create();
        Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => Assert.True(activation.TryRequestOpen()))))
            .GetAwaiter().GetResult();
        Pump();
        var opens = 0;
        activation.SetReady(() => { dispatcher.VerifyAccess(); opens++; return Task.CompletedTask; });
        Assert.Equal(0, opens);
        Pump();
        Assert.Equal(1, opens);
        Assert.True(activation.TryRequestOpen());
        Pump();
        Assert.Equal(2, opens);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Stop_discards_pending_and_queued_requests_and_rejects_new_ones(bool ready) => OnSta(() =>
    {
        using var activation = Create();
        var opens = 0;
        if (ready) activation.SetReady(() => { opens++; return Task.CompletedTask; });
        Assert.True(activation.TryRequestOpen());
        activation.Dispose();
        if (!ready) activation.SetReady(() => { opens++; return Task.CompletedTask; });
        Assert.False(activation.TryRequestOpen());
        Pump();
        Assert.Equal(0, opens);
    });

    [Fact]
    public void Async_failure_does_not_escape_dispatcher_or_block_later_Open() => OnSta(() =>
    {
        using var activation = Create();
        var opens = 0;
        activation.SetReady(async () => { opens++; await Task.Yield(); throw new InvalidOperationException("open failed"); });
        activation.TryRequestOpen();
        Pump();
        activation.TryRequestOpen();
        Pump();
        Assert.Equal(2, opens);
    });

    private static OpenCommandDispatcher Create() => new(Dispatcher.CurrentDispatcher,
        new PluginLog(Path.Combine(TestOutputPaths.TempDirectory, "activation-" + Guid.NewGuid().ToString("N"))));

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(failure);
    }
}
