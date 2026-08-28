using System.Windows.Threading;
using CircleToSearch.Capture;
using Xunit;
using GdiBitmap = System.Drawing.Bitmap;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Tests;

public sealed class OverlayWindowTests
{
    [Fact]
    public void Overlay_opens_and_shuts_down_without_dispatcher_exception()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(64, 48);
            var monitor = new GdiRectangle(0, 0, 64, 48);
            var overlay = new OverlayWindow(frame, monitor, monitor, 1.0, new OverlayOptions(8, 12));
            overlay.Show();
            PumpUntilShutdown(overlay);
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Overlay_on_negative_coordinates_monitor_opens_without_dispatcher_exception()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(100, 80);
            var monitor = new GdiRectangle(-1920, -80, 100, 80);
            var workArea = new GdiRectangle(-1920, -80, 100, 50);
            var overlay = new OverlayWindow(frame, monitor, workArea, 1.25, new OverlayOptions(8, 12));
            overlay.Show();
            PumpUntilShutdown(overlay);
        });

        Assert.Null(failure);
    }

    private static void PumpUntilShutdown(OverlayWindow overlay)
    {
        overlay.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
        Dispatcher.Run();
    }

    private static Exception? RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true,
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30));
        Assert.True(!thread.IsAlive, "the STA thread did not finish in time");
        return failure;
    }
}
