using System.Drawing;
using System.Windows.Threading;
using CircleToSearch.Capture;
using CircleToSearch.Capture.OverlayInteractions;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class ColorPickControllerTests
{
    [Fact]
    public void Pick_copies_uppercase_rgb_ignores_alpha_and_confirms_once()
    {
        Assert.Null(RunOnSta(() =>
        {
            using var frame = new Bitmap(2, 2);
            frame.SetPixel(1, 1, Color.FromArgb(0x17, 0x3A, 0x7B, 0xD5));
            var clipboard = new List<string>();
            var notifications = new List<ToastNotification>();
            var started = 0;
            var completed = 0;
            using var controller = new ColorPickController(
                frame,
                clipboard.Add,
                TestUiStrings.English,
                notifications.Add,
                () => started++,
                () => throw new Xunit.Sdk.XunitException("pick unexpectedly failed"),
                () => completed++);

            controller.Pick(new Point(1, 1));
            controller.Pick(new Point(0, 0));

            Assert.Equal(["#3A7BD5"], clipboard);
            var notification = Assert.Single(notifications);
            Assert.Equal(TestUiStrings.English.ColorCopied, notification.Message);
            Assert.Equal(new ToastColorSample(0x3A, 0x7B, 0xD5), notification.ColorSample);
            Assert.Equal(1, started);
            Assert.True(controller.HasPendingConfirmation);
            PumpFor(TimeSpan.FromMilliseconds(900));
            Assert.Equal(1, completed);
            Assert.False(controller.HasPendingConfirmation);
        }));
    }

    [Theory]
    [InlineData(-10, -20, "#102030")]
    [InlineData(10, -20, "#405060")]
    [InlineData(-10, 20, "#708090")]
    [InlineData(10, 20, "#A0B0C0")]
    public void Pick_clamps_all_frame_edges(int x, int y, string expected)
    {
        Assert.Null(RunOnSta(() =>
        {
            using var frame = new Bitmap(2, 2);
            frame.SetPixel(0, 0, Color.FromArgb(0x10, 0x20, 0x30));
            frame.SetPixel(1, 0, Color.FromArgb(0x40, 0x50, 0x60));
            frame.SetPixel(0, 1, Color.FromArgb(0x70, 0x80, 0x90));
            frame.SetPixel(1, 1, Color.FromArgb(0xA0, 0xB0, 0xC0));
            string? copied = null;
            using var controller = Create(frame, value => copied = value);

            controller.Pick(new Point(x, y));

            Assert.Equal(expected, copied);
        }));
    }

    [Fact]
    public void Clipboard_failure_shows_error_and_allows_retry()
    {
        Assert.Null(RunOnSta(() =>
        {
            using var frame = new Bitmap(1, 1);
            frame.SetPixel(0, 0, Color.Red);
            var attempts = 0;
            var started = 0;
            var failed = 0;
            var notifications = new List<ToastNotification>();
            using var controller = new ColorPickController(
                frame,
                _ =>
                {
                    attempts++;
                    if (attempts == 1) throw new InvalidOperationException("clipboard busy");
                },
                TestUiStrings.English,
                notifications.Add,
                () => started++,
                () => failed++,
                () => { });

            controller.Pick(new Point());
            Assert.Equal(1, failed);
            Assert.Equal(0, started);
            Assert.False(controller.HasPendingConfirmation);
            Assert.Equal(TestUiStrings.English.ColorCopyFailed, Assert.Single(notifications).Message);

            controller.Pick(new Point());
            Assert.Equal(2, attempts);
            Assert.Equal(1, started);
            Assert.True(controller.HasPendingConfirmation);
            Assert.Equal(2, notifications.Count);
        }));
    }

    [Fact]
    public void Dispose_cancels_pending_confirmation_callback()
    {
        Assert.Null(RunOnSta(() =>
        {
            using var frame = new Bitmap(1, 1);
            var completed = 0;
            var controller = Create(frame, _ => { }, () => completed++);
            controller.Pick(new Point());
            Assert.True(controller.HasPendingConfirmation);

            controller.Dispose();
            Assert.False(controller.HasPendingConfirmation);
            PumpFor(TimeSpan.FromMilliseconds(900));
            Assert.Equal(0, completed);
        }));
    }

    private static ColorPickController Create(
        Bitmap frame,
        Action<string> clipboard,
        Action? completed = null) =>
        new(
            frame,
            clipboard,
            TestUiStrings.English,
            _ => { },
            () => { },
            () => { },
            completed ?? (() => { }));

    private static void PumpFor(TimeSpan duration)
    {
        var dispatcherFrame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            dispatcherFrame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(dispatcherFrame);
    }

    private static Exception? RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(10));
        Assert.False(thread.IsAlive, "the STA thread did not finish in time");
        return failure;
    }
}
