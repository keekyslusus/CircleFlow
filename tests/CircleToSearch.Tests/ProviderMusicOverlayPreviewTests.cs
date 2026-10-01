using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.Capture;
using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Audio;
using CircleToSearch.MusicRecognition.Shazam;
using CircleToSearch.Search;
using CircleToSearch.Ui.Effects;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class ProviderMusicOverlayPreviewTests
{
    [SkippableFact]
    public void Renders_provider_listening_and_result_states()
    {
        TestSwitches.Require("CTS_PROVIDER_MUSIC_PREVIEW");
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
        SearchProviderDescriptor[] providers =
        [
            new(SearchProviderIds.GoogleLens, "Google Lens"),
            new(SearchProviderIds.YandexImages, "Yandex Images"),
        ];
        var visual = OverlayVisualFactory.CreateRoot(
            null,
            new Size(width, height),
            32,
            lightTheme,
            TestUiStrings.English,
            providers,
            SearchProviderIds.GoogleLens);
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

        ProviderMenuVisualPresenter.SetOpen(visual.Provider!, visual.Bottom.Root, true);
        Pump(TimeSpan.FromMilliseconds(220));
        Capture(visual.Root, $"provider-music-{themeName}-provider.png");
        ProviderMenuVisualPresenter.SetOpen(visual.Provider!, visual.Bottom.Root, false);

        visual.Selection.Screenshot.Opacity = 0;
        visual.Selection.Sheen.Opacity = 0;
        visual.Selection.Halo.Opacity = 0;
        visual.Selection.Accent.Opacity = 0;
        MusicOverlayVisualPresenter.SetListeningState(visual.Music, listening: true, lightTheme);
        visual.Music.Waveform.Start();
        visual.Music.Waveform.Report(new MusicVisualizationFrame(
            TimeSpan.FromMilliseconds(33), 0.68, 0, IsTransient: false));
        visual.Effects.SceneRipples.Emit(new SceneRippleRequest(
            new Point(width / 2.0, height * 0.45),
            SceneRipplePreset.AudioTransient,
            0.86));
        Pump(TimeSpan.FromMilliseconds(90));
        Capture(visual.Root, $"provider-music-{themeName}-listening-enter.png");
        Pump(TimeSpan.FromMilliseconds(250));
        Capture(visual.Root, $"provider-music-{themeName}-listening.png");
        visual.Music.Waveform.Stop();
        MusicOverlayVisualPresenter.SetListeningState(visual.Music, listening: false, lightTheme);
        Pump(TimeSpan.FromMilliseconds(90));
        Capture(visual.Root, $"provider-music-{themeName}-listening-exit.png");
        Pump(TimeSpan.FromMilliseconds(110));

        var matchCard = MusicOverlayVisualPresenter.PresentResult(
            visual.Music,
            MusicRecognitionOutcome.Matched(new ShazamRecognition(
                "Midnight Signal", "The Satellites", null, null, null, null,
                "https://www.shazam.com/track/1")),
            TestUiStrings.English,
            lightTheme,
            _ => { },
            (_, _) => { });
        window.UpdateLayout();
        var matchOrigin = matchCard.TransformToAncestor(visual.Root).Transform(
            new Point(matchCard.ActualWidth / 2, matchCard.ActualHeight / 2));
        visual.Effects.SceneRipples.Emit(new SceneRippleRequest(
            matchOrigin,
            SceneRipplePreset.MusicMatch,
            1));
        Pump(TimeSpan.FromMilliseconds(420));
        Capture(visual.Root, $"provider-music-{themeName}-match.png");

        MusicOverlayVisualPresenter.PresentResult(
            visual.Music,
            MusicRecognitionOutcome.From(MusicRecognitionStatus.NoAudio),
            TestUiStrings.English,
            lightTheme,
            _ => { },
            (_, _) => { });
        window.UpdateLayout();
        Capture(visual.Root, $"provider-music-{themeName}-no-audio.png");
        visual.Music.LoadingIndicator.Dispose();
        window.Close();
    }

    private static void Capture(FrameworkElement root, string fileName)
    {
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
