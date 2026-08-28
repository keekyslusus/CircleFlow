using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Capture;
using Xunit;
using GdiBitmap = System.Drawing.Bitmap;
using GdiColor = System.Drawing.Color;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Tests;

public sealed class ChipPreviewTests
{
    // Renders the real overlay content to a PNG for visual checks without triggering
    // a selection: CTS_CHIP_PREVIEW=1 dotnet test --filter ChipPreviewTests.
    // The PNG lands at %TEMP%\cts-chip-preview.png (150% DPI, like a scaled laptop screen).
    [Fact]
    public void Renders_chip_preview_png()
    {
        if (Environment.GetEnvironmentVariable("CTS_CHIP_PREVIEW") != "1") return;

        var path = Path.Combine(Path.GetTempPath(), "cts-chip-preview.png");
        var failure = RunOnSta(() => Render(path));
        Assert.Null(failure);
    }

    private static void Render(string path)
    {
        using var frame = new GdiBitmap(960, 600);
        using (var graphics = System.Drawing.Graphics.FromImage(frame))
        {
            using var gradient = new System.Drawing.Drawing2D.LinearGradientBrush(
                new GdiRectangle(0, 0, 960, 600),
                GdiColor.FromArgb(234, 238, 243),
                GdiColor.FromArgb(34, 38, 50),
                35f);
            graphics.FillRectangle(gradient, 0, 0, 960, 600);
        }

        var monitor = new GdiRectangle(0, 0, 960, 600);
        var overlay = new OverlayWindow(frame, monitor, monitor, 1.5, new OverlayOptions(8, 12));
        overlay.Show();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            overlay.UpdateLayout();
            var bitmap = new RenderTargetBitmap(
                (int)Math.Ceiling(overlay.ActualWidth * 1.5),
                (int)Math.Ceiling(overlay.ActualHeight * 1.5),
                144,
                144,
                PixelFormats.Pbgra32);
            bitmap.Render((Visual)overlay.Content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(path);
            encoder.Save(file);
            overlay.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
        };
        timer.Start();
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
