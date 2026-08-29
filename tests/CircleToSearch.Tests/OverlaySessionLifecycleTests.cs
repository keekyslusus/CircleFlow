using System.Drawing;
using System.Threading.Channels;
using System.Windows.Threading;
using CircleToSearch.Capture;
using CircleToSearch.Search;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class OverlaySessionLifecycleTests
{
    private static readonly SearchProviderDescriptor[] Providers =
    [
        new(SearchProviderIds.GoogleLens, "Google Lens"),
        new(SearchProviderIds.YandexImages, "Yandex Images"),
    ];

    [Fact]
    public async Task Dispatcher_crash_faults_channel_and_dispose_does_not_wait_for_dead_thread()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CircleToSearch.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var ready = new TaskCompletionSource<OverlaySession>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => RunCrashingOverlay(directory, ready, stopped))
        {
            IsBackground = true,
            Name = "crashing overlay test",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var session = await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var closed = await Assert.ThrowsAsync<ChannelClosedException>(
            () => session.ReadCommandAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));

        var failure = Assert.IsType<InvalidOperationException>(closed.InnerException);
        Assert.Equal("simulated dispatcher failure", failure.Message);
        await session.CloseAsync().WaitAsync(TimeSpan.FromSeconds(2));
        await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(thread.Join(TimeSpan.FromSeconds(2)), "The crashed overlay thread did not exit.");
    }

    private static void RunCrashingOverlay(
        string logDirectory,
        TaskCompletionSource<OverlaySession> ready,
        TaskCompletionSource stopped)
    {
        using var frame = new Bitmap(640, 400);
        var monitor = new Rectangle(0, 0, 640, 400);
        var log = new PluginLog(logDirectory);
        var session = new OverlaySession(log);
        try
        {
            var window = new OverlayWindow(
                frame,
                monitor,
                monitor,
                1,
                new OverlayLaunchOptions(
                    new OverlayOptions(8, 12),
                    TestUiStrings.English,
                    Providers,
                    SearchProviderIds.GoogleLens),
                session.Publish,
                overscan: false);
            session.Attach(window);
            window.Show();
            window.Dispatcher.BeginInvoke(
                () => throw new InvalidOperationException("simulated dispatcher failure"),
                DispatcherPriority.ContextIdle);
            ready.TrySetResult(session);
            try
            {
                OverlaySessionFactory.RunDispatcherLoop(session);
            }
            catch (InvalidOperationException exception) when (exception.Message == "simulated dispatcher failure")
            {
            }
        }
        catch (Exception exception)
        {
            session.Complete(exception);
            ready.TrySetException(exception);
        }
        finally
        {
            session.Complete();
            stopped.TrySetResult();
        }
    }
}
