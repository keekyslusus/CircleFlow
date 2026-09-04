using System.Drawing;
using CircleToSearch.TextRecognition;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class OcrTextHitTesterTests
{
    [Fact]
    public void Selects_inside_and_tolerance_edge_but_not_a_large_gap()
    {
        var document = Document(
            new OcrWord(0, 0, 0, "en", "small", new Rectangle(10, 10, 20, 10)),
            new OcrWord(1, 0, 1, "en", "next", new Rectangle(60, 10, 25, 10)));
        var tester = new OcrTextHitTester(3);

        Assert.Equal("small", tester.HitTest(document, new Point(15, 15))?.Text);
        Assert.Equal("small", tester.HitTest(document, new Point(7, 15))?.Text);
        Assert.Null(tester.HitTest(document, new Point(45, 15)));
    }

    [Fact]
    public void Overlap_prefers_smallest_then_reading_order()
    {
        var document = Document(
            new OcrWord(0, 0, 0, "en", "large", new Rectangle(0, 0, 40, 20)),
            new OcrWord(1, 0, 1, "en", "small", new Rectangle(10, 5, 10, 10)));

        Assert.Equal("small", new OcrTextHitTester().HitTest(document, new Point(15, 10))?.Text);
    }

    private static OcrDocument Document(params OcrWord[] words) => new(
        "en-US",
        new Size(100, 50),
        [new OcrLine(0, 0, "en", words.Select(word => word.BoundsPx).Aggregate(Rectangle.Union), words)]);
}
