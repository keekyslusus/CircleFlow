using System.Drawing;
using CircleToSearch.TextRecognition;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class OcrFrameTilerTests
{
    [Fact]
    public void Frame_below_limit_has_one_identity_tile()
    {
        var tile = Assert.Single(new OcrFrameTiler().Create(1920, 1080, 2600));

        Assert.Equal(new Rectangle(0, 0, 1920, 1080), tile.BoundsPx);
        Assert.Equal(tile.BoundsPx, tile.CoreBoundsPx);
    }

    [Fact]
    public void Four_k_frame_is_fully_covered_without_oversized_tiles_or_core_gaps()
    {
        var tiles = new OcrFrameTiler().Create(3840, 2160, 2600);

        Assert.Equal(2, tiles.Count);
        Assert.All(tiles, tile =>
        {
            Assert.InRange(tile.BoundsPx.Width, 1, 2600);
            Assert.InRange(tile.BoundsPx.Height, 1, 2600);
        });
        Assert.Equal(64, Rectangle.Intersect(tiles[0].BoundsPx, tiles[1].BoundsPx).Width);
        for (var x = 0; x < 3840; x++)
            Assert.Equal(1, tiles.Count(tile => tile.CoreBoundsPx.Contains(x, 1000)));
    }

    [Fact]
    public void Boundary_word_is_owned_by_exactly_one_tile()
    {
        var tiles = new OcrFrameTiler().Create(3840, 2160, 2600);
        var boundary = tiles[0].CoreBoundsPx.Right;
        var word = new Rectangle(boundary - 4, 100, 8, 20);

        Assert.Equal(1, tiles.Count(tile => tile.Owns(word)));
    }
}
