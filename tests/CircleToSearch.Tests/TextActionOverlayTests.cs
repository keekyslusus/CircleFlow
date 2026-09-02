using System.Windows;
using CircleToSearch.Capture;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class TextActionOverlayTests
{
    [Fact]
    public void Card_prefers_above_then_below_and_clamps_to_overlay()
    {
        var viewport = new Size(200, 100);
        var card = new Size(80, 30);

        Assert.Equal(new Point(60, 22), TextActionCardLayout.Place(new Rect(80, 60, 40, 10), card, viewport));
        Assert.Equal(new Point(0, 18), TextActionCardLayout.Place(new Rect(-10, 0, 20, 10), card, viewport));
        Assert.Equal(new Point(120, 57), TextActionCardLayout.Place(new Rect(190, 95, 10, 5), card, viewport));
    }
}
