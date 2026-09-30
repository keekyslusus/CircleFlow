using System.Windows;
using CircleToSearch.Capture;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class FloatingToolbarLayoutTests
{
    [Fact]
    public void Toolbar_prefers_above_then_below_and_keeps_an_edge_margin()
    {
        var viewport = new Size(200, 100);
        var toolbar = new Size(80, 30);

        Assert.Equal(new Point(60, 22), FloatingToolbarLayout.Place(new Rect(80, 60, 40, 10), toolbar, viewport));
        Assert.Equal(new Point(16, 18), FloatingToolbarLayout.Place(new Rect(-10, 0, 20, 10), toolbar, viewport));
        Assert.Equal(new Point(104, 54), FloatingToolbarLayout.Place(new Rect(190, 95, 10, 5), toolbar, viewport));
    }

    [Fact]
    public void Selection_filling_the_screen_puts_the_toolbar_inside_above_the_taskbar()
    {
        var viewport = new Size(1920, 1080);
        var toolbar = new Size(420, 44);
        var taskbar = new Thickness(0, 0, 0, 48);

        Assert.Equal(new Point(750, 956),
            FloatingToolbarLayout.Place(new Rect(0, 0, 1920, 1080), toolbar, viewport, taskbar));
        Assert.Equal(new Point(750, 924),
            FloatingToolbarLayout.Place(new Rect(0, 10, 1920, 990), toolbar, viewport, taskbar));
    }

    [Fact]
    public void Toolbar_below_a_selection_never_lands_on_the_taskbar()
    {
        var viewport = new Size(1920, 1080);
        var toolbar = new Size(420, 44);

        var placement = FloatingToolbarLayout.Place(
            new Rect(400, 20, 600, 970), toolbar, viewport, new Thickness(0, 0, 0, 48));

        Assert.True(placement.Y + toolbar.Height <= 1080 - 48 - 16);
    }

    [Fact]
    public void Toolbar_stays_clear_of_side_and_top_taskbars()
    {
        var viewport = new Size(1920, 1080);
        var toolbar = new Size(420, 44);

        Assert.Equal(new Point(64, 248),
            FloatingToolbarLayout.Place(new Rect(0, 300, 100, 100), toolbar, viewport, new Thickness(48, 0, 0, 0)));
        Assert.Equal(new Point(590, 188),
            FloatingToolbarLayout.Place(new Rect(700, 80, 200, 100), toolbar, viewport, new Thickness(0, 48, 0, 0)));
    }

    [Fact]
    public void Available_width_leaves_room_for_side_taskbars_and_edge_margins()
    {
        Assert.Equal(1872 - 32, FloatingToolbarLayout.AvailableWidth(1920, new Thickness(48, 0, 0, 0)));
        Assert.Equal(0, FloatingToolbarLayout.AvailableWidth(20, default));
    }

    [Fact]
    public void Oversized_toolbar_clamps_to_the_viewport_origin()
    {
        var placement = FloatingToolbarLayout.Place(
            new Rect(40, 30, 20, 10),
            new Size(240, 120),
            new Size(100, 80));

        Assert.Equal(new Point(0, 0), placement);
    }
}
