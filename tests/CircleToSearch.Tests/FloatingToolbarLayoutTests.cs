using System.Windows;
using CircleToSearch.Capture;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class FloatingToolbarLayoutTests
{
    [Fact]
    public void Toolbar_prefers_above_then_below_and_clamps_to_overlay()
    {
        var viewport = new Size(200, 100);
        var toolbar = new Size(80, 30);

        Assert.Equal(new Point(60, 22), FloatingToolbarLayout.Place(new Rect(80, 60, 40, 10), toolbar, viewport));
        Assert.Equal(new Point(0, 18), FloatingToolbarLayout.Place(new Rect(-10, 0, 20, 10), toolbar, viewport));
        Assert.Equal(new Point(120, 57), FloatingToolbarLayout.Place(new Rect(190, 95, 10, 5), toolbar, viewport));
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
