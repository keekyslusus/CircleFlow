using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using CircleToSearch.Capture;
using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Shazam;
using CircleToSearch.Ui;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class MusicOverlayVisualTests
{
    [Theory]
    [InlineData(false, 960)]
    [InlineData(true, 960)]
    [InlineData(false, 360)]
    public void Match_card_fits_metadata_and_viewport_without_overlapping_actions(bool lightTheme, double viewportWidth)
    {
        Assert.Null(RunOnSta(() =>
        {
            var visual = MusicOverlayVisualFactory.Create(new Size(viewportWidth, 400), lightTheme, TestUiStrings.English);
            var host = new Grid();
            host.Children.Add(visual.ResultHost);
            var widths = new List<double>();
            var tracks = new[]
            {
                new ShazamRecognition("Go", "M83", null, null, null, null, "https://www.shazam.com/track/1"),
                new ShazamRecognition("Midnight City", "M83", "Hurry Up, We're Dreaming", null, null, null, "https://www.shazam.com/track/1"),
                new ShazamRecognition(new string('W', 180), new string('A', 180), new string('B', 180), null, null, "invalid", "https://www.shazam.com/track/1"),
            };
            try
            {
                for (var index = 0; index < tracks.Length; index++)
                {
                    var commands = new List<IOverlayCommand>();
                    string? copied = null;
                    var card = MusicOverlayVisualPresenter.PresentResult(visual,
                        MusicRecognitionOutcome.Matched(tracks[index]), TestUiStrings.English, lightTheme,
                        commands.Add, (text, _) => copied = text);
                    host.Measure(new Size(viewportWidth, 180));
                    host.Arrange(new Rect(0, 0, viewportWidth, 180));
                    host.UpdateLayout();
                    widths.Add(card.ActualWidth);
                    Assert.InRange(card.ActualWidth, Math.Min(360, viewportWidth - 32), Math.Min(640, viewportWidth - 32));
                    Assert.Equal(120, card.ActualHeight);
                    var texts = Descendants(card).OfType<TextBlock>().ToArray();
                    Assert.Equal(index == 0 ? 2 : 3, texts.Length);
                    var buttons = Descendants(card).OfType<Button>().ToArray();
                    var copy = buttons.Single(button => AutomationProperties.GetName(button) == TestUiStrings.English.CopyTrackInfo);
                    var close = buttons.Single(button => AutomationProperties.GetName(button) == TestUiStrings.English.Close);
                    var open = buttons.Single(button => AutomationProperties.GetName(button) == TestUiStrings.English.OpenInShazam);
                    Assert.True(texts[0].TranslatePoint(new Point(texts[0].ActualWidth, 0), card).X <= copy.TranslatePoint(new Point(), card).X);
                    copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.Equal($"{tracks[index].Title} - {tracks[index].Artist}", copied);
                    Assert.Empty(commands);
                    close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.IsType<DismissMusicResult>(Assert.Single(commands));
                    commands.Clear();
                    open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.IsType<OpenMusicResult>(Assert.Single(commands));

                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)viewportWidth, 180, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(host);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    System.IO.Directory.CreateDirectory(TestOutputPaths.TempDirectory);
                    using var output = System.IO.File.Create(System.IO.Path.Combine(TestOutputPaths.TempDirectory, $"music-card-{lightTheme}-{viewportWidth}-{index}.png"));
                    encoder.Save(output);
                }
                Assert.Equal(Math.Min(360, viewportWidth - 32), widths[0]);
                Assert.Equal(widths[0], widths[1]);
                Assert.True(widths[2] >= widths[1]);
                if (viewportWidth > 392) Assert.True(widths[2] > widths[1]);
            }
            finally
            {
                visual.LoadingIndicator.Dispose();
                visual.Waveform.Dispose();
            }
        }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Listening_label_stays_light_with_a_dark_shadow_in_both_themes(bool lightTheme)
    {
        var failure = RunOnSta(() =>
        {
            var root = OverlayVisualFactory.CreateRoot(
                null,
                new Size(640, 400),
                32,
                lightTheme,
                TestUiStrings.English);
            var label = Assert.Single(root.Music.ListeningLayer.Children.OfType<TextBlock>());
            var foreground = Assert.IsType<SolidColorBrush>(label.Foreground);
            var shadow = Assert.IsType<DropShadowEffect>(label.Effect);

            Assert.Equal(PluginPalette.ListeningText, foreground.Color);
            Assert.Equal(PluginPalette.OpaqueBlack, shadow.Color);
            root.Music.Waveform.Dispose();
            root.Effects.SceneRipples.Dispose();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Listening_and_matched_result_states_are_owned_by_the_music_visual()
    {
        var failure = RunOnSta(() =>
        {
            var root = OverlayVisualFactory.CreateRoot(
                null,
                new Size(640, 400),
                32,
                lightTheme: false,
                TestUiStrings.English);
            MusicOverlayVisualPresenter.SetListeningState(root.Music, listening: true, lightTheme: false);
            Assert.Equal(Visibility.Visible, root.Music.ListeningLayer.Visibility);

            MusicOverlayVisualPresenter.SetListeningState(root.Music, listening: false, lightTheme: false);
            Assert.Equal(Visibility.Collapsed, root.Music.ListeningLayer.Visibility);

            string? copiedTrack = null;
            var card = MusicOverlayVisualPresenter.PresentResult(
                root.Music,
                MusicRecognitionOutcome.Matched(new ShazamRecognition(
                    "Track", "Artist", null, null, null, null, "https://www.shazam.com/track/1")),
                TestUiStrings.English,
                lightTheme: false,
                _ => { },
                (track, _) => copiedTrack = track);

            Assert.Equal(Visibility.Visible, root.Music.ResultHost.Visibility);
            root.Root.Measure(new Size(640, 400));
            root.Root.Arrange(new Rect(0, 0, 640, 400));
            Assert.Equal(120, card.ActualHeight);
            Assert.Equal(new[] { "Track", "Artist" }, Descendants(card).OfType<TextBlock>().Select(text => text.Text));
            var names = Descendants(root.Music.ResultHost).OfType<Button>()
                .Select(AutomationProperties.GetName)
                .ToArray();
            Assert.Contains(TestUiStrings.English.Close, names);
            Assert.Contains(TestUiStrings.English.CopyTrackInfo, names);
            Assert.Contains(TestUiStrings.English.OpenInShazam, names);
            Descendants(root.Music.ResultHost).OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == TestUiStrings.English.CopyTrackInfo)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Track - Artist", copiedTrack);
            root.Music.LoadingIndicator.Dispose();
            root.Music.Waveform.Dispose();
            root.Effects.SceneRipples.Dispose();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Music_button_glyph_switches_between_note_and_loading_shape()
    {
        var failure = RunOnSta(() =>
        {
            var root = OverlayVisualFactory.CreateRoot(
                null,
                new Size(640, 400),
                32,
                lightTheme: false,
                TestUiStrings.English);

            MusicOverlayVisualPresenter.SetListeningState(
                root.Music,
                listening: true,
                lightTheme: false,
                animationsEnabled: false);

            Assert.Equal(0, root.Music.Icon.Opacity);
            Assert.Equal(Visibility.Visible, root.Music.LoadingIndicator.Visibility);
            Assert.Equal(1, root.Music.LoadingIndicator.Opacity);
            Assert.False(root.Music.LoadingIndicator.IsRendering);

            MusicOverlayVisualPresenter.SetListeningState(
                root.Music,
                listening: false,
                lightTheme: false,
                animationsEnabled: false);

            Assert.Equal(1, root.Music.Icon.Opacity);
            Assert.Equal(Visibility.Collapsed, root.Music.LoadingIndicator.Visibility);
            Assert.Equal(0, root.Music.LoadingIndicator.Opacity);
            root.Music.LoadingIndicator.Dispose();
            root.Music.Waveform.Dispose();
            root.Effects.SceneRipples.Dispose();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Animated_loading_shape_releases_the_render_loop_on_stop()
    {
        var failure = RunOnSta(() =>
        {
            var indicator = new LoadingIndicatorVisual();

            indicator.Start(animationsEnabled: true);
            Assert.True(indicator.IsRendering);

            indicator.Stop();
            Assert.False(indicator.IsRendering);
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Loading_shape_viewport_center_matches_the_music_button_center()
    {
        var failure = RunOnSta(() =>
        {
            var root = OverlayVisualFactory.CreateRoot(
                null,
                new Size(640, 400),
                32,
                lightTheme: false,
                TestUiStrings.English);
            var window = new Window
            {
                Content = root.Root,
                Width = 640,
                Height = 400,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
            };
            MusicOverlayVisualPresenter.SetListeningState(
                root.Music, listening: true, lightTheme: false, animationsEnabled: false);
            window.Show();
            window.UpdateLayout();

            var buttonCenter = root.Music.Button.TranslatePoint(
                new Point(root.Music.Button.ActualWidth / 2, root.Music.Button.ActualHeight / 2),
                root.Root);
            var shapeCenter = root.Music.LoadingIndicator.TranslatePoint(
                new Point(
                    root.Music.LoadingIndicator.ActualWidth / 2,
                    root.Music.LoadingIndicator.ActualHeight / 2),
                root.Root);

            Assert.Equal(buttonCenter.X, shapeCenter.X, precision: 6);
            Assert.Equal(buttonCenter.Y, shapeCenter.Y, precision: 6);
            MusicOverlayVisualPresenter.SetListeningState(
                root.Music, listening: false, lightTheme: false, animationsEnabled: false);
            window.Close();
            root.Music.LoadingIndicator.Dispose();
            root.Music.Waveform.Dispose();
            root.Effects.SceneRipples.Dispose();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Rapid_listening_reentry_keeps_the_loading_shape_alive()
    {
        var failure = RunOnSta(() =>
        {
            var root = OverlayVisualFactory.CreateRoot(
                null,
                new Size(640, 400),
                32,
                lightTheme: false,
                TestUiStrings.English);

            MusicOverlayVisualPresenter.SetListeningState(
                root.Music, listening: true, lightTheme: false, animationsEnabled: true);
            MusicOverlayVisualPresenter.SetListeningState(
                root.Music, listening: false, lightTheme: false, animationsEnabled: true);
            MusicOverlayVisualPresenter.SetListeningState(
                root.Music, listening: true, lightTheme: false, animationsEnabled: true);
            Pump(TimeSpan.FromMilliseconds(260));

            Assert.True(root.Music.LoadingIndicator.IsRequestedActive);
            Assert.True(root.Music.LoadingIndicator.IsRendering);
            Assert.Equal(Visibility.Visible, root.Music.LoadingIndicator.Visibility);

            MusicOverlayVisualPresenter.SetListeningState(
                root.Music, listening: false, lightTheme: false, animationsEnabled: false);
            root.Music.LoadingIndicator.Dispose();
            root.Music.Waveform.Dispose();
            root.Effects.SceneRipples.Dispose();
        });

        Assert.Null(failure);
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

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
