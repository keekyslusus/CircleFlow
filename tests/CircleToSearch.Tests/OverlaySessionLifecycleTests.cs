using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CircleToSearch.Capture;
using CircleToSearch.Capture.OverlayInteractions;
using CircleToSearch.Interop;
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
        var directory = Path.Combine(TestOutputPaths.TempDirectory, "CircleToSearch.Tests", Guid.NewGuid().ToString("N"));
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

    [Fact]
    public async Task Dispatcher_crash_during_selection_hold_does_not_dispose_transferred_frame()
    {
        var directory = TestOutputPaths.NewTempDirectory(
            nameof(Dispatcher_crash_during_selection_hold_does_not_dispose_transferred_frame));
        var frame = new Bitmap(640, 400);
        var monitor = new Rectangle(0, 0, 640, 400);
        var windowFactory = new CrashAfterSelectionWindowFactory();
        var sessionFactory = new OverlaySessionFactory(
            new PluginLog(directory),
            new SingleCapture(new PointerMonitorCaptureResult(
                monitor,
                monitor,
                1,
                new System.Drawing.Point(320, 200),
                frame)),
            windowFactory);
        var opened = await sessionFactory.OpenAsync(
                new OverlayLaunchOptions(
                    new OverlayOptions(8, 12),
                    TestUiStrings.English,
                    Providers,
                    SearchProviderIds.GoogleLens),
                CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(opened);
        await using var session = opened;
        var window = await windowFactory.Window.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(NativeMethods.GetCursorPos(out var originalPointer));
        VisualSelection? selection = null;
        try
        {
            var points = await window.Dispatcher.InvokeAsync(() =>
            {
                window.UpdateLayout();
                var input = window.VisualState.Selection.InputSurface;
                return (
                    Input: input,
                    Start: input.PointToScreen(new System.Windows.Point(80, 80)),
                    Finish: input.PointToScreen(new System.Windows.Point(300, 220)));
            });
            Assert.True(SetCursorPos((int)Math.Round(points.Start.X), (int)Math.Round(points.Start.Y)));
            mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
            await WaitUntilAsync(
                async () => await window.Dispatcher.InvokeAsync(() => ReferenceEquals(Mouse.Captured, points.Input)),
                TimeSpan.FromSeconds(2));
            Assert.True(SetCursorPos((int)Math.Round(points.Finish.X), (int)Math.Round(points.Finish.Y)));
            mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);

            Assert.IsType<VisualSelectionStarted>(
                await session.ReadCommandAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));
            selection = Assert.IsType<VisualSelection>(
                await session.ReadCommandAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));
            var closed = await Assert.ThrowsAsync<ChannelClosedException>(
                () => session.ReadCommandAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));

            var failure = Assert.IsType<InvalidOperationException>(closed.InnerException);
            Assert.Equal("failure during selection hold", failure.Message);
            Assert.True(window.FrameTransferred);
            Assert.Same(frame, selection.Selection.FrozenFrame);
            Assert.Equal(640, selection.Selection.FrozenFrame.Width);
            Assert.Equal(400, selection.Selection.FrozenFrame.Height);
        }
        finally
        {
            SetCursorPos(originalPointer.X, originalPointer.Y);
            selection?.Selection.Dispose();
        }

        Assert.Throws<ArgumentException>(() => frame.GetHbitmap());
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
                TestOverlayControllers.CreateFactory(),
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

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(10);
        }
        Assert.Fail("the expected WPF input state was not reached in time");
    }

    private sealed class SingleCapture(PointerMonitorCaptureResult result) : IPointerMonitorCapture
    {
        public PointerMonitorCaptureResult? Capture() => result;
    }

    private sealed class CrashAfterSelectionWindowFactory : IOverlayWindowFactory
    {
        internal TaskCompletionSource<OverlayWindow> Window { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public OverlayWindow Create(
            Bitmap frame,
            Rectangle monitor,
            Rectangle workArea,
            double scale,
            OverlayLaunchOptions options,
            Action<IOverlayCommand> publishCommand,
            System.Drawing.Point entranceOrigin)
        {
            OverlayWindow? window = null;
            window = new OverlayWindow(
                frame,
                monitor,
                workArea,
                scale,
                options,
                command =>
                {
                    publishCommand(command);
                    if (command is not VisualSelection) return;
                    window!.Dispatcher.BeginInvoke(
                        () => throw new InvalidOperationException("failure during selection hold"),
                        DispatcherPriority.ContextIdle);
                },
                TestOverlayControllers.CreateFactory(),
                overscan: false,
                entranceOrigin: entranceOrigin);
            Window.TrySetResult(window);
            return window;
        }
    }

    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
}
