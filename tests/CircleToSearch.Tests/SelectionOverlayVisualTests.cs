using System.Windows;
using System.Windows.Media;
using CircleToSearch.Capture;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class SelectionOverlayVisualTests
{
    [Fact]
    public void Reveal_geometry_handles_empty_and_valid_polygons()
    {
        var failure = RunOnSta(() =>
        {
            var size = new Size(200, 120);
            var empty = Assert.IsType<StreamGeometry>(
                SelectionOverlayTransitions.BuildRevealGeometry(size, []));
            Assert.Equal(FillRule.EvenOdd, empty.FillRule);
            Assert.True(empty.FillContains(new Point(100, 60)));

            Point[] polygon =
            [
                new(40, 30),
                new(160, 30),
                new(160, 90),
                new(40, 90),
            ];
            var reveal = Assert.IsType<StreamGeometry>(
                SelectionOverlayTransitions.BuildRevealGeometry(size, polygon));
            Assert.True(reveal.FillContains(new Point(10, 10)));
            Assert.False(reveal.FillContains(new Point(100, 60)));
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Polygon_and_final_frame_geometry_keep_their_fill_and_radius()
    {
        var failure = RunOnSta(() =>
        {
            Assert.Same(Geometry.Empty, SelectionOverlayTransitions.BuildPolygonGeometry([]));
            Assert.Same(
                Geometry.Empty,
                SelectionOverlayTransitions.BuildPolygonGeometry([new Point(1, 1)]));

            var polygon = Assert.IsType<StreamGeometry>(
                SelectionOverlayTransitions.BuildPolygonGeometry(
                    [new Point(10, 10), new Point(90, 10), new Point(50, 80)]));
            Assert.Equal(FillRule.Nonzero, polygon.FillRule);
            Assert.True(polygon.FillContains(new Point(50, 30)));

            var frame = Assert.IsType<RectangleGeometry>(
                SelectionOverlayTransitions.BuildSelectionFrameGeometry(new Rect(10, 20, 80, 60)));
            Assert.Equal(SelectionOverlayVisualFactory.FrameCornerRadius, frame.RadiusX);
            Assert.Equal(SelectionOverlayVisualFactory.FrameCornerRadius, frame.RadiusY);
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Selection_reveal_targets_the_final_mask_and_frame()
    {
        var failure = RunOnSta(() =>
        {
            var visual = OverlayVisualFactory.CreateRoot(
                null,
                new Size(200, 120),
                16,
                lightTheme: false,
                TestUiStrings.English);
            var reveal = SelectionOverlayTransitions.BuildRevealGeometry(
                new Size(200, 120),
                [new Point(10, 10), new Point(90, 10), new Point(90, 80)]);
            var frame = SelectionOverlayTransitions.BuildSelectionFrameGeometry(new Rect(10, 10, 80, 70));

            SelectionOverlayTransitions.BeginSelectionReveal(visual.Selection, reveal, frame);

            Assert.Same(reveal, visual.Selection.DimRect.Data);
            Assert.Same(frame, visual.Selection.SelectionFrame.Data);
            if (OverlayVisualResources.AnimationsEnabled())
            {
                Assert.True(visual.Selection.Dim.HasAnimatedProperties);
                Assert.True(visual.Selection.SelectionFrame.HasAnimatedProperties);
            }
            else
            {
                Assert.Equal(0, visual.Selection.Dim.Opacity);
                Assert.Equal(1, visual.Selection.DimRect.Opacity);
                Assert.Equal(1, visual.Selection.SelectionFrame.Opacity);
            }
            visual.Music.Waveform.Dispose();
            visual.Effects.SceneRipples.Dispose();
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
