using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Capture;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class ChipPreviewTests
{
    // Renders the real chip visual to PNGs for visual checks without triggering
    // a selection: CTS_CHIP_PREVIEW=1 dotnet test --filter ChipPreviewTests.
    // Output: %TEMP%\cts-chip-preview.png (dark) and cts-chip-preview-light.png, 150% DPI.
    [Fact]
    public void Renders_chip_preview_pngs_for_both_themes()
    {
        if (Environment.GetEnvironmentVariable("CTS_CHIP_PREVIEW") != "1") return;

        Assert.Null(RunOnSta(() => Render(
            Path.Combine(Path.GetTempPath(), "cts-chip-preview.png"),
            lightTheme: false)));
        Assert.Null(RunOnSta(() => Render(
            Path.Combine(Path.GetTempPath(), "cts-chip-preview-light.png"),
            lightTheme: true)));
    }

    private static void Render(string path, bool lightTheme)
    {
        var visual = OverlayVisualFactory.CreateRoot(null, new Size(640, 400), 32, lightTheme);
        // The screenshot layer is empty in the preview, so give the root a desktop-like gradient.
        visual.Root.Background = new LinearGradientBrush(
            Color.FromRgb(0xEA, 0xEE, 0xF3),
            Color.FromRgb(0x22, 0x26, 0x32),
            35);
        var window = new Window
        {
            Content = visual.Root,
            Width = 640,
            Height = 400,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
        };
        window.Show();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            visual.Root.UpdateLayout();
            var bitmap = new RenderTargetBitmap(960, 600, 144, 144, PixelFormats.Pbgra32);
            bitmap.Render(visual.Root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(path);
            encoder.Save(file);
            window.Dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
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
