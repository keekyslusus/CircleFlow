using System.Windows;
using System.Windows.Controls;
using CircleToSearch.Capture;
using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Shazam;
using CircleToSearch.Search;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class OverlayVisualCompositionTests
{
    private static readonly SearchProviderDescriptor[] Providers =
    [
        new(SearchProviderIds.GoogleLens, "Google Lens"),
        new(SearchProviderIds.YandexImages, "Yandex Images"),
    ];

    [Fact]
    public void Root_layers_keep_the_overlay_z_order_and_hit_test_boundaries()
    {
        var failure = RunOnSta(() =>
        {
            var visual = OverlayVisualFactory.CreateRoot(
                null,
                new Size(640, 400),
                32,
                lightTheme: false,
                TestUiStrings.English);

            UIElement[] expected =
            [
                visual.Selection.Screenshot,
                visual.Selection.Dim,
                visual.Selection.DimRect,
                visual.Selection.Sheen,
                visual.Selection.Halo,
                visual.Selection.Accent,
                visual.Selection.SelectionFrame,
                visual.Selection.InputSurface,
                visual.Effects.SceneRippleLayer,
                visual.Music.ListeningLayer,
                visual.Bottom.Root,
                visual.Music.DebugPanel,
            ];

            Assert.Equal(expected, visual.Root.Children.Cast<UIElement>());
            Assert.All(expected.Take(7), layer => Assert.False(layer.IsHitTestVisible));
            Assert.True(visual.Selection.InputSurface.IsHitTestVisible);
            Assert.True(visual.Bottom.Root.IsHitTestVisible);
            Assert.Null(visual.Bottom.Root.Background);
            Assert.Equal(
                [visual.Music.ResultHost, visual.Actions.Tray],
                visual.Bottom.Stack.Children.Cast<UIElement>());
            Assert.Equal(new Thickness(0, 0, 0, 16), visual.Music.ResultHost.Margin);
            visual.Music.Waveform.Dispose();
            visual.Effects.SceneRipples.Dispose();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void No_providers_produces_a_two_item_tray_and_no_provider_group()
    {
        var failure = RunOnSta(() =>
        {
            var visual = OverlayVisualFactory.CreateRoot(
                null,
                new Size(640, 400),
                32,
                lightTheme: false,
                TestUiStrings.English);

            Assert.Null(visual.Provider);
            Assert.Equal(2, visual.Actions.Tray.Children.Count);
            Assert.Same(visual.Actions.Chip, visual.Actions.Tray.Children[0]);
            Assert.Same(visual.Music.Button, visual.Actions.Tray.Children[1]);
            Assert.Empty(visual.Bottom.ProviderMenuLayer.Children);
            visual.Music.Waveform.Dispose();
            visual.Effects.SceneRipples.Dispose();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Provider_menu_floats_outside_the_bottom_stack()
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

            Assert.Same(visual.Provider!.Menu, Assert.Single(visual.Bottom.ProviderMenuLayer.Children));
            Assert.DoesNotContain(visual.Provider.Menu, visual.Bottom.Stack.Children.Cast<UIElement>());
            Assert.Same(visual.Bottom.ProviderMenuLayer, visual.Provider.Menu.Parent);
            visual.Music.Waveform.Dispose();
            visual.Effects.SceneRipples.Dispose();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Bottom_stack_keeps_the_tray_anchored_while_results_grow_upward()
    {
        var failure = RunOnSta(() =>
        {
            const double bottomMargin = 32;
            var visual = OverlayVisualFactory.CreateRoot(
                null,
                new Size(640, 400),
                bottomMargin,
                lightTheme: false,
                TestUiStrings.English);
            Arrange(visual.Root, new Size(640, 400));

            var collapsedTray = BoundsInRoot(visual.Actions.Tray, visual.Root);
            Assert.Equal(400 - bottomMargin, collapsedTray.Bottom, 6);
            Assert.Equal(visual.Actions.Tray.ActualHeight, visual.Bottom.Stack.ActualHeight, 6);

            var match = MusicOverlayVisualPresenter.PresentResult(
                visual.Music,
                MusicRecognitionOutcome.Matched(new ShazamRecognition(
                    "Track", "Artist", null, null, null, null, "https://www.shazam.com/track/1")),
                TestUiStrings.English,
                lightTheme: false,
                _ => { },
                (_, _) => { });
            Arrange(visual.Root, new Size(640, 400));

            var matchedTray = BoundsInRoot(visual.Actions.Tray, visual.Root);
            var matchedResult = BoundsInRoot(match, visual.Root);
            Assert.Equal(collapsedTray.Bottom, matchedTray.Bottom, 6);
            Assert.Equal(collapsedTray.Top, matchedTray.Top, 6);
            Assert.Equal(48, matchedResult.Height, 6);
            Assert.Equal(16, matchedTray.Top - matchedResult.Bottom, 6);

            var state = MusicOverlayVisualPresenter.PresentResult(
                visual.Music,
                MusicRecognitionOutcome.From(MusicRecognitionStatus.NoAudio),
                TestUiStrings.English,
                lightTheme: false,
                _ => { },
                (_, _) => { });
            Arrange(visual.Root, new Size(640, 400));

            var stateTray = BoundsInRoot(visual.Actions.Tray, visual.Root);
            var stateResult = BoundsInRoot(state, visual.Root);
            Assert.Equal(collapsedTray.Bottom, stateTray.Bottom, 6);
            Assert.Equal(16, stateTray.Top - stateResult.Bottom, 6);
            Assert.True(stateResult.Height > matchedResult.Height);
            Assert.True(stateResult.Top < matchedResult.Top);

            visual.Music.ResultHost.Visibility = Visibility.Collapsed;
            Arrange(visual.Root, new Size(640, 400));
            var restoredTray = BoundsInRoot(visual.Actions.Tray, visual.Root);
            Assert.Equal(collapsedTray.Top, restoredTray.Top, 6);
            Assert.Equal(collapsedTray.Bottom, restoredTray.Bottom, 6);
            Assert.Equal(visual.Actions.Tray.ActualHeight, visual.Bottom.Stack.ActualHeight, 6);
            visual.Music.Waveform.Dispose();
            visual.Effects.SceneRipples.Dispose();
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Provider_configuration_requires_the_initial_provider_to_exist()
    {
        var failure = RunOnSta(() =>
        {
            SearchProviderDescriptor[] providers = [new(SearchProviderIds.GoogleLens, "Google Lens")];
            Assert.Throws<InvalidOperationException>(() => OverlayVisualFactory.CreateRoot(
                null,
                new Size(640, 400),
                32,
                lightTheme: false,
                TestUiStrings.English,
                providers,
                SearchProviderIds.YandexImages));
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

    private static void Arrange(FrameworkElement root, Size size)
    {
        root.Measure(size);
        root.Arrange(new Rect(size));
        root.UpdateLayout();
    }

    private static Rect BoundsInRoot(FrameworkElement element, FrameworkElement root)
    {
        var origin = element.TranslatePoint(new Point(), root);
        return new Rect(origin, new Size(element.ActualWidth, element.ActualHeight));
    }
}
