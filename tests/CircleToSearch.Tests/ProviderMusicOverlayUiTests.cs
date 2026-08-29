using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using CircleToSearch.Capture;
using CircleToSearch.Search;
using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Shazam;
using Xunit;
using GdiBitmap = System.Drawing.Bitmap;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Tests;

public sealed class ProviderMusicOverlayUiTests
{
    private static readonly SearchProviderDescriptor[] Providers =
    [
        new(SearchProviderIds.GoogleLens, "Google Lens"),
        new(SearchProviderIds.YandexImages, "Yandex Images"),
    ];

    [Fact]
    public void Action_tray_places_provider_between_selection_and_music_and_menu_excludes_current()
    {
        var failure = RunOnSta(() =>
        {
            var visual = OverlayVisualFactory.CreateRoot(
                null,
                new Size(640, 400),
                32,
                lightTheme: false,
                TestUiStrings.English,
                Providers,
                SearchProviderIds.GoogleLens);

            Assert.Same(visual.Chip, visual.ActionTray.Children[0]);
            Assert.Same(visual.ProviderButton, visual.ActionTray.Children[1]);
            Assert.Same(visual.MusicButton, visual.ActionTray.Children[2]);
            Assert.Equal(44, visual.ProviderButton!.Height);
            Assert.Equal(44, visual.MusicButton.Height);
            Assert.NotNull(visual.ProviderChevron);
            Assert.Equal(170, visual.Waveform.Width);
            Assert.Equal(40, visual.Waveform.Height);
            Assert.Equal(220, visual.ProviderMenu.Width);
            Assert.Equal(new CornerRadius(16), visual.ProviderMenu.CornerRadius);
            var item = Assert.Single(((StackPanel)visual.ProviderMenu.Child).Children.OfType<Button>());
            Assert.Equal(SearchProviderIds.YandexImages, item.Tag);
            Assert.Equal(36, item.MinHeight);
            Assert.Contains(
                "Yandex Images",
                Descendants((DependencyObject)item.Content).OfType<TextBlock>().Select(text => text.Text));
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Music_click_enters_listening_without_shutting_down_and_provider_remains_usable()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(640, 400);
            var monitor = new GdiRectangle(0, 0, 640, 400);
            var commands = new List<IOverlayCommand>();
            var overlay = new OverlayWindow(
                frame,
                monitor,
                monitor,
                1,
                new OverlayLaunchOptions(
                    new OverlayOptions(8, 12),
                    TestUiStrings.English,
                    Providers,
                    SearchProviderIds.GoogleLens),
                commands.Add,
                overscan: false);
            overlay.Show();
            overlay.UpdateLayout();

            var buttons = Descendants((DependencyObject)overlay.Content).OfType<Button>().ToArray();
            var music = Assert.Single(buttons, button =>
                AutomationProperties.GetName(button) == TestUiStrings.English.MusicRecognitionAction);
            music.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(OverlayInteractionMode.Listening, overlay.Mode);
            Assert.IsType<StartMusicRecognition>(Assert.Single(commands));
            Assert.False(overlay.Dispatcher.HasShutdownStarted);

            var provider = Assert.Single(buttons, button => ReferenceEquals(button.Tag, null) &&
                AutomationProperties.GetName(button).Contains("choose provider", StringComparison.Ordinal));
            provider.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            overlay.UpdateLayout();
            var yandex = Descendants((DependencyObject)overlay.Content).OfType<Button>()
                .Single(button => Equals(button.Tag, SearchProviderIds.YandexImages));
            yandex.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(SearchProviderIds.YandexImages,
                Assert.IsType<ProviderSelected>(commands[1]).ProviderId);
            Assert.Equal(OverlayInteractionMode.Listening, overlay.Mode);
            overlay.CloseFromSession();
            Dispatcher.Run();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Match_result_stops_waveform_keeps_dim_and_exposes_actions()
    {
        var failure = RunOnSta(() =>
        {
            using var frame = new GdiBitmap(640, 400);
            var monitor = new GdiRectangle(0, 0, 640, 400);
            var overlay = new OverlayWindow(
                frame,
                monitor,
                monitor,
                1,
                new OverlayLaunchOptions(
                    new OverlayOptions(8, 12),
                    TestUiStrings.English,
                    Providers,
                    SearchProviderIds.GoogleLens),
                _ => { },
                overscan: false);
            overlay.Show();
            overlay.UpdateLayout();

            overlay.ShowListening();
            Assert.Equal(Visibility.Visible, overlay.VisualState.ListeningLayer.Visibility);
            Assert.Equal(1, overlay.VisualState.Dim.Opacity);
            if (OverlayVisualFactory.AnimationsEnabled())
                Assert.True(overlay.VisualState.Screenshot.HasAnimatedProperties);

            overlay.ShowMusicResult(MusicRecognitionOutcome.Matched(new ShazamRecognition(
                "Track", "Artist", null, null, null, null, "https://www.shazam.com/track/1")));
            overlay.UpdateLayout();

            Assert.Equal(OverlayInteractionMode.MusicResult, overlay.Mode);
            Assert.False(overlay.VisualState.Waveform.IsRendering);
            Assert.Equal(Visibility.Collapsed, overlay.VisualState.ListeningLayer.Visibility);
            Assert.Equal(Visibility.Visible, overlay.VisualState.ResultHost.Visibility);
            Assert.Equal(VerticalAlignment.Bottom, overlay.VisualState.ResultHost.VerticalAlignment);
            var card = Assert.Single(overlay.VisualState.ResultHost.Children.OfType<Border>());
            Assert.Equal(48, card.Height);
            Assert.Equal(new CornerRadius(24), card.CornerRadius);
            var actionNames = Descendants(overlay.VisualState.ResultHost).OfType<Button>()
                .Select(AutomationProperties.GetName)
                .ToArray();
            Assert.Contains(TestUiStrings.English.CopyTrackInfo, actionNames);
            Assert.Contains(TestUiStrings.English.OpenInShazam, actionNames);
            Assert.Contains(TestUiStrings.English.Close, actionNames);
            Assert.All(
                Descendants(overlay.VisualState.ResultHost).OfType<Button>(),
                button => Assert.Equal(30, button.Width));
            overlay.CloseFromSession();
            Dispatcher.Run();
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
        thread.Join(TimeSpan.FromSeconds(20));
        Assert.False(thread.IsAlive, "the STA thread did not finish in time");
        return failure;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
