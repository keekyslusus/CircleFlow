using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Capture;
using CircleToSearch.Capture.OverlayInteractions;
using CircleToSearch.MusicRecognition;
using Xunit;

namespace CircleToSearch.Tests;

// CTS_TOAST_PREVIEW=1 dotnet test --filter ToastOverlayPreviewTests
// Output: tests/temp/toast-preview-{dark,light}-*.png at 150% DPI.
public sealed class ToastOverlayPreviewTests
{
    [Fact]
    public void Renders_toast_motion_stacking_and_music_result_states()
    {
        if (Environment.GetEnvironmentVariable("CTS_TOAST_PREVIEW") != "1") return;
        Directory.CreateDirectory(TestOutputPaths.TempDirectory);
        Assert.Null(RunOnSta(() =>
        {
            RenderTheme(lightTheme: false, "dark");
            RenderTheme(lightTheme: true, "light");
        }));
    }

    private static void RenderTheme(bool lightTheme, string themeName)
    {
        const int width = 960;
        const int height = 600;
        var visual = OverlayVisualFactory.CreateRoot(
            null,
            new Size(width, height),
            32,
            lightTheme,
            TestUiStrings.English);
        visual.Root.Background = new LinearGradientBrush(
            lightTheme ? Color.FromRgb(0xEE, 0xF2, 0xF7) : Color.FromRgb(0x4B, 0x51, 0x5C),
            lightTheme ? Color.FromRgb(0xA9, 0xB5, 0xC7) : Color.FromRgb(0x12, 0x15, 0x1B),
            30);
        var window = new Window
        {
            Content = visual.Root,
            Width = width,
            Height = height,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
        };
        window.Show();
        window.UpdateLayout();
        using var controller = new ToastOverlayController(visual.Bottom, lightTheme, () => true);

        controller.Show(new ToastNotification(
            TestUiStrings.English.SelectionTooSmall,
            ToastTone.Error,
            TimeSpan.FromSeconds(30)));
        Pump(TimeSpan.FromMilliseconds(35));
        Capture(visual.Root, $"toast-preview-{themeName}-entrance.png");
        Pump(TimeSpan.FromMilliseconds(220));
        Capture(visual.Root, $"toast-preview-{themeName}-settled.png");

        controller.Show(new ToastNotification(
            "The selection is ready to search with the active provider.",
            ToastTone.Success,
            TimeSpan.FromSeconds(30)));
        Pump(TimeSpan.FromMilliseconds(220));
        Capture(visual.Root, $"toast-preview-{themeName}-stacked.png");

        visual.Bottom.LayoutTransitions.Apply(() => MusicOverlayVisualPresenter.PresentResult(
            visual.Music,
            MusicRecognitionOutcome.From(MusicRecognitionStatus.NoAudio),
            TestUiStrings.English,
            lightTheme,
            _ => { },
            (_, _) => { }),
            animationsEnabled: true);
        Pump(TimeSpan.FromMilliseconds(55));
        Capture(visual.Root, $"toast-preview-{themeName}-flip.png");
        Pump(TimeSpan.FromMilliseconds(230));
        Capture(visual.Root, $"toast-preview-{themeName}-music.png");

        controller.Show(new ToastNotification(
            "This neutral notification is leaving.",
            ToastTone.Neutral,
            TimeSpan.FromMilliseconds(40)));
        Pump(TimeSpan.FromMilliseconds(105));
        Capture(visual.Root, $"toast-preview-{themeName}-exit.png");
        visual.Effects.SceneRipples.Dispose();
        visual.Music.Waveform.Dispose();
        window.Content = null;
        window.Close();
    }

    private static void Capture(FrameworkElement root, string fileName)
    {
        root.UpdateLayout();
        const double scale = 1.5;
        var bitmap = new RenderTargetBitmap(
            (int)Math.Round(root.ActualWidth * scale),
            (int)Math.Round(root.ActualHeight * scale),
            96 * scale,
            96 * scale,
            PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(TestOutputPaths.TempDirectory, fileName));
        encoder.Save(stream);
    }

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
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
        thread.Join(TimeSpan.FromSeconds(30));
        Assert.False(thread.IsAlive, "the STA thread did not finish in time");
        return failure;
    }
}
