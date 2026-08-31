using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using CircleToSearch.Capture;
using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Shazam;
using CircleToSearch.Ui;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class MusicOverlayVisualTests
{
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
            Assert.Equal(
                Enum.GetValues<MusicDebugScenario>(),
                root.Music.DebugScenarioButtons.Children.OfType<Button>()
                    .Select(button => Assert.IsType<MusicDebugScenario>(button.Tag))
                    .ToArray());

            MusicOverlayVisualPresenter.SetListeningState(root.Music, listening: false, lightTheme: false);
            Assert.Equal(Visibility.Collapsed, root.Music.ListeningLayer.Visibility);

            var card = MusicOverlayVisualPresenter.PresentResult(
                root.Music,
                MusicRecognitionOutcome.Matched(new ShazamRecognition(
                    "Track", "Artist", null, null, null, null, "https://www.shazam.com/track/1")),
                TestUiStrings.English,
                lightTheme: false,
                _ => { },
                (_, _) => { });

            Assert.Equal(Visibility.Visible, root.Music.ResultHost.Visibility);
            Assert.Equal(48, card.Height);
            var names = Descendants(root.Music.ResultHost).OfType<Button>()
                .Select(AutomationProperties.GetName)
                .ToArray();
            Assert.Contains(TestUiStrings.English.Close, names);
            Assert.Contains(TestUiStrings.English.CopyTrackInfo, names);
            Assert.Contains(TestUiStrings.English.OpenInShazam, names);
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
