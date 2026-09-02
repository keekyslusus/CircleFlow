using System.Windows;
using CircleToSearch.Capture;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class TranslationCardLayoutTests
{
    [Fact]
    public void Cards_are_clamped_and_separated_inside_viewport()
    {
        var placements = TranslationCardLayout.Place(
        [
            (new Rect(-20, 70, 50, 10), new Size(100, 25)),
            (new Rect(150, 75, 30, 10), new Size(80, 25)),
        ], new Size(200, 120));

        Assert.All(placements, card =>
        {
            Assert.True(card.Left >= 8);
            Assert.True(card.Right <= 192);
            Assert.True(card.Top >= 8);
            Assert.True(card.Bottom <= 112);
        });
        Assert.True(placements[1].Top >= placements[0].Bottom + 4);
    }
}
