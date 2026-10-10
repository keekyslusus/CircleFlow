using System.Windows;
using CircleToSearch.Capture;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class OverlayZoomCameraTests
{
    private static readonly Size Viewport = new(1000, 600);

    [Fact]
    public void Wheel_notches_ease_toward_their_scale_and_keep_the_pivot_under_the_pointer()
    {
        var camera = Camera();
        var pivot = new Point(700, 200);
        var scene = camera.ToScene(pivot);

        camera.ZoomBy(2, pivot);
        camera.Advance(0.016);
        Assert.InRange(camera.Scale, 1.01, 1.25 * 1.25 - 0.01);
        Settle(camera);

        Assert.Equal(1.25 * 1.25, camera.Scale, 6);
        Assert.Equal(pivot.X, camera.ToViewport(scene).X, 6);
        Assert.Equal(pivot.Y, camera.ToViewport(scene).Y, 6);
        Assert.False(camera.IsMoving);
        Assert.True(camera.IsZoomed);
    }

    [Fact]
    public void Zoom_stays_between_the_unzoomed_screen_and_the_maximum()
    {
        var camera = Camera();

        camera.ZoomBy(-3, new Point(500, 300));
        Settle(camera);
        Assert.Equal(1, camera.Scale);
        Assert.False(camera.IsZoomed);

        camera.ZoomBy(100, new Point(500, 300));
        Settle(camera);
        Assert.Equal(OverlayZoomCamera.MaxScale, camera.Scale, 6);
    }

    [Fact]
    public void Pushing_past_the_maximum_overshoots_a_little_and_springs_back()
    {
        var camera = Camera();
        Assert.False(camera.ZoomBy(100, new Point(500, 300)));
        Settle(camera);
        var pivot = new Point(200, 450);
        var scene = camera.ToScene(pivot);

        Assert.True(camera.ZoomBy(1, pivot));
        var peak = 0.0;
        var zoomed = 0.0;
        for (var frame = 0; frame < 600 && camera.IsMoving; frame++)
        {
            zoomed += camera.Advance(0.016);
            peak = Math.Max(peak, camera.Scale);
            AssertNear(pivot, camera.ToViewport(scene));
            AssertCovers(camera);
        }

        Assert.InRange(peak / OverlayZoomCamera.MaxScale, 1.02, 1.08);
        Assert.Equal(OverlayZoomCamera.MaxScale, camera.Scale, 6);
        Assert.Equal(0, zoomed);
        Assert.False(camera.ZoomBy(-1, pivot));
    }

    [Fact]
    public void Pushing_below_the_unzoomed_screen_backs_away_around_the_center_and_returns()
    {
        var camera = Camera();
        var center = new Point(Viewport.Width / 2, Viewport.Height / 2);

        Assert.True(camera.ZoomBy(-1, new Point(900, 50)));
        var lowest = 1.0;
        var zoomed = 0.0;
        for (var frame = 0; frame < 600 && camera.IsMoving; frame++)
        {
            zoomed += camera.Advance(0.016);
            lowest = Math.Min(lowest, camera.Scale);
            Assert.True(camera.Scale <= 1);
            AssertNear(center, camera.ToViewport(center));
        }

        Assert.InRange(lowest, 0.85, 0.995);
        Assert.Equal(1, camera.Scale, 6);
        Assert.Equal(default, camera.Offset);
        Assert.Equal(0, zoomed);
        Assert.False(camera.IsZoomed);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1000, 600)]
    [InlineData(1000, 0)]
    public void Spamming_zoom_out_from_a_corner_opens_no_gap_until_the_screen_is_back_to_normal(double x, double y)
    {
        var camera = Camera();
        camera.ZoomBy(8, new Point(x, y));
        Settle(camera);
        camera.ZoomBy(-8, new Point(x, y));
        var bounced = false;

        for (var frame = 0; frame < 300; frame++)
        {
            var scaleBefore = camera.Scale;
            var bounce = camera.ZoomBy(-1, new Point(x, y));
            if (bounce) Assert.True(scaleBefore <= 1.02, $"bounced at {scaleBefore}");
            bounced |= bounce;
            camera.Advance(0.016);
            // The backdrop turns black for any gap; this only keeps a gap from opening while still magnified.
            if (!camera.CoversScreen) Assert.True(camera.Scale <= 1.02, $"gap at {camera.Scale}");
        }

        Assert.True(bounced);
        Settle(camera);
        Assert.True(camera.CoversScreen);
        Assert.Equal(1, camera.Scale, 6);
    }

    [Fact]
    public void Repeated_pushes_below_the_screen_stay_a_small_bounce()
    {
        var camera = Camera();
        var lowest = 1.0;

        for (var push = 0; push < 10; push++)
        {
            camera.ZoomBy(-1, new Point(500, 300));
            camera.Advance(0.016);
            lowest = Math.Min(lowest, camera.Scale);
        }
        Settle(camera);

        Assert.InRange(lowest, 0.85, 1);
        Assert.Equal(1, camera.Scale, 6);

        camera.ZoomBy(100, new Point(500, 300));
        Settle(camera);
        var highest = 0.0;
        for (var push = 0; push < 10; push++)
        {
            camera.ZoomBy(1, new Point(500, 300));
            camera.Advance(0.016);
            highest = Math.Max(highest, camera.Scale);
            AssertCovers(camera);
        }
        Settle(camera);

        Assert.InRange(highest / OverlayZoomCamera.MaxScale, 1, 1.08 + 1e-9);
        Assert.Equal(OverlayZoomCamera.MaxScale, camera.Scale, 6);
    }

    [Fact]
    public void Advance_reports_the_signed_zoom_travel()
    {
        var camera = Camera();
        camera.ZoomBy(2, new Point(500, 300));
        var travel = 0.0;
        for (var frame = 0; frame < 600 && camera.IsMoving; frame++) travel += camera.Advance(0.016);

        Assert.Equal(2 * Math.Log(1.25), travel, 6);
        camera.Reset();
        Assert.True(camera.Advance(0.016) < 0);
    }

    [Fact]
    public void Turning_the_wheel_back_drops_the_rest_of_the_previous_zoom()
    {
        var camera = Camera();
        camera.ZoomBy(2, new Point(500, 300));
        Settle(camera);
        camera.ZoomBy(4, new Point(500, 300));
        camera.Advance(0.016);
        var reached = camera.Scale;

        camera.ZoomBy(-1, new Point(500, 300));
        Settle(camera);

        Assert.Equal(reached / 1.25, camera.Scale, 6);
    }

    [Fact]
    public void Zooming_out_near_an_edge_keeps_the_screen_covering_the_viewport()
    {
        var camera = Camera();
        camera.ZoomBy(6, new Point(0, 0));
        Settle(camera);
        camera.ZoomBy(-3, new Point(1000, 600));

        for (var frame = 0; frame < 120; frame++)
        {
            camera.Advance(0.016);
            AssertCovers(camera);
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1002, 602)]
    public void A_screen_inset_by_the_overscan_edge_stays_flush_with_it_at_any_corner(double x, double y)
    {
        // The overlay window reaches one dip past the monitor, where the screenshot starts.
        var screen = new Rect(1, 1, 1000, 600);
        var camera = new OverlayZoomCamera();
        camera.SetScreen(screen);

        camera.ZoomBy(100, new Point(x, y));
        Settle(camera);
        camera.PanBy(new Vector(x == 0 ? 500 : -500, y == 0 ? 500 : -500));

        var topLeft = camera.ToViewport(screen.TopLeft);
        var bottomRight = camera.ToViewport(screen.BottomRight);
        Assert.True(topLeft.X <= screen.Left + 1e-9 && topLeft.Y <= screen.Top + 1e-9, $"{topLeft}");
        Assert.True(bottomRight.X >= screen.Right - 1e-9 && bottomRight.Y >= screen.Bottom - 1e-9, $"{bottomRight}");
        Assert.True(camera.CoversScreen);
        AssertNear(x == 0 ? screen.TopLeft : screen.BottomRight, x == 0 ? topLeft : bottomRight);

        Assert.True(camera.Reset());
        Settle(camera);
        Assert.Equal(1, camera.Scale, 6);
        Assert.Equal(0, camera.Offset.Length, 6);
    }

    [Fact]
    public void Reset_returns_exactly_to_the_unzoomed_screen()
    {
        var camera = Camera();
        camera.ZoomBy(5, new Point(830, 120));
        Settle(camera);
        camera.PanBy(new Vector(-90, 40));

        Assert.True(camera.Reset());
        Settle(camera);

        Assert.Equal(1, camera.Scale);
        Assert.Equal(default, camera.Offset);
    }

    [Fact]
    public void Reset_reports_nothing_to_undo_when_unzoomed_or_already_returning()
    {
        var camera = Camera();
        Assert.False(camera.Reset());

        camera.ZoomBy(3, new Point(400, 300));
        Settle(camera);
        Assert.True(camera.Reset());
        camera.Advance(0.016);

        Assert.True(camera.IsZoomed);
        Assert.False(camera.Reset());
    }

    [Fact]
    public void A_fling_glides_with_friction_and_stops_at_the_edge()
    {
        var camera = Camera();
        camera.ZoomBy(4, new Point(500, 300));
        Settle(camera);
        var start = camera.Offset;

        camera.Fling(new Vector(-400, 0));
        camera.Advance(0.1);
        var afterFirst = camera.Offset.X;
        camera.Advance(0.1);
        var afterSecond = camera.Offset.X;

        Assert.True(afterFirst < start.X);
        Assert.True(start.X - afterFirst > afterFirst - afterSecond);
        camera.Fling(new Vector(-100000, 0));
        Settle(camera);
        Assert.Equal(Viewport.Width * (1 - camera.Scale), camera.Offset.X, 6);
        Assert.False(camera.IsMoving);
    }

    [Fact]
    public void Dragging_the_unzoomed_screen_does_not_move_it()
    {
        var camera = Camera();

        camera.PanBy(new Vector(120, -80));

        Assert.Equal(default, camera.Offset);
    }

    [Fact]
    public void Viewport_and_scene_points_convert_both_ways()
    {
        var camera = Camera();
        camera.ZoomBy(3, new Point(310, 470));
        Settle(camera);
        var point = new Point(123.5, 456.25);

        var roundTrip = camera.ToScene(camera.ToViewport(point));
        var rect = camera.ToViewport(new Rect(100, 100, 50, 20));

        Assert.Equal(point.X, roundTrip.X, 6);
        Assert.Equal(point.Y, roundTrip.Y, 6);
        Assert.Equal(50 * camera.Scale, rect.Width, 6);
        Assert.Equal(camera.ToViewport(new Point(100, 100)), rect.TopLeft);
    }

    private static OverlayZoomCamera Camera()
    {
        var camera = new OverlayZoomCamera();
        camera.SetScreen(new Rect(Viewport));
        return camera;
    }

    private static void Settle(OverlayZoomCamera camera)
    {
        for (var frame = 0; frame < 600 && camera.IsMoving; frame++) camera.Advance(0.016);
        Assert.False(camera.IsMoving);
    }

    private static void AssertNear(Point expected, Point actual)
    {
        Assert.Equal(expected.X, actual.X, 6);
        Assert.Equal(expected.Y, actual.Y, 6);
    }

    private static void AssertCovers(OverlayZoomCamera camera)
    {
        var topLeft = camera.ToViewport(new Point(0, 0));
        var bottomRight = camera.ToViewport(new Point(Viewport.Width, Viewport.Height));
        Assert.True(topLeft.X <= 1e-9 && topLeft.Y <= 1e-9);
        Assert.True(bottomRight.X >= Viewport.Width - 1e-9 && bottomRight.Y >= Viewport.Height - 1e-9);
    }
}
