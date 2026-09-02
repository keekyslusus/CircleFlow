namespace CircleToSearch.Tests;

using System.Windows;
using CircleToSearch.Ocr;
using Xunit;
using GdiRectangle = System.Drawing.Rectangle;

public sealed class OcrSpatialIndexTests
{
    [Fact]
    public void Empty_snapshot_returns_null_and_empty_ranges()
    {
        var snapshot = OcrScreenSnapshot.Empty;
        Assert.Null(snapshot.FindWordAt(new Point(10, 10)));
        Assert.Empty(snapshot.FindWordsInRange(new Point(0, 0), new Point(100, 100)));
    }

    [Fact]
    public void FindWordAt_returns_matching_word_when_point_is_inside()
    {
        var word1 = new OcrWordSnapshot("Hello", new Rect(10, 10, 40, 20), new GdiRectangle(10, 10, 40, 20));
        var word2 = new OcrWordSnapshot("World", new Rect(60, 10, 50, 20), new GdiRectangle(60, 10, 50, 20));
        var line = new OcrLineSnapshot("Hello World", new Rect(10, 10, 100, 20), new GdiRectangle(10, 10, 100, 20), [word1, word2]);
        var snapshot = new OcrScreenSnapshot([line], [word1, word2], "Hello World");

        var hit = snapshot.FindWordAt(new Point(25, 15));
        Assert.NotNull(hit);
        Assert.Equal("Hello", hit.Text);

        var miss = snapshot.FindWordAt(new Point(55, 15));
        Assert.Null(miss);
    }

    [Fact]
    public void FindWordsInRange_selects_all_words_between_start_and_end()
    {
        var word1 = new OcrWordSnapshot("The", new Rect(0, 0, 30, 20), new GdiRectangle(0, 0, 30, 20));
        var word2 = new OcrWordSnapshot("quick", new Rect(35, 0, 40, 20), new GdiRectangle(35, 0, 40, 20));
        var word3 = new OcrWordSnapshot("brown", new Rect(80, 0, 40, 20), new GdiRectangle(80, 0, 40, 20));
        var word4 = new OcrWordSnapshot("fox", new Rect(125, 0, 30, 20), new GdiRectangle(125, 0, 30, 20));
        var line = new OcrLineSnapshot("The quick brown fox", new Rect(0, 0, 155, 20), new GdiRectangle(0, 0, 155, 20), [word1, word2, word3, word4]);
        var snapshot = new OcrScreenSnapshot([line], [word1, word2, word3, word4], "The quick brown fox");

        var forward = snapshot.FindWordsInRange(new Point(10, 10), new Point(90, 10));
        Assert.Equal(3, forward.Count);
        Assert.Equal("The", forward[0].Text);
        Assert.Equal("quick", forward[1].Text);
        Assert.Equal("brown", forward[2].Text);

        var backward = snapshot.FindWordsInRange(new Point(130, 10), new Point(40, 10));
        Assert.Equal(3, backward.Count);
        Assert.Equal("quick", backward[0].Text);
        Assert.Equal("brown", backward[1].Text);
        Assert.Equal("fox", backward[2].Text);
    }
}
