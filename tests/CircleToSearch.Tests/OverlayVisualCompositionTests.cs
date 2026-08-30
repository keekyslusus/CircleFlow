using System.Windows;
using CircleToSearch.Capture;
using CircleToSearch.Search;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class OverlayVisualCompositionTests
{
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
                visual.Actions.Root,
                visual.Music.ResultHost,
                visual.Music.DebugPanel,
            ];

            Assert.Equal(expected, visual.Root.Children.Cast<UIElement>());
            Assert.All(expected.Take(7), layer => Assert.False(layer.IsHitTestVisible));
            Assert.True(visual.Selection.InputSurface.IsHitTestVisible);
            Assert.True(visual.Actions.Root.IsHitTestVisible);
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
}
