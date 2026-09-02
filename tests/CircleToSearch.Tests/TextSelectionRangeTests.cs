using System.Drawing;
using CircleToSearch.TextRecognition;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class TextSelectionRangeTests
{
    [Fact]
    public void Reconstructs_multiline_text_in_reading_order()
    {
        var first = new OcrWord(0, 0, 0, "Hello,", new Rectangle(1, 1, 20, 10));
        var second = new OcrWord(1, 0, 1, "world", new Rectangle(24, 1, 20, 10));
        var third = new OcrWord(2, 1, 2, "世界", new Rectangle(1, 20, 20, 10));
        var document = new OcrDocument("en", new Size(100, 100),
        [
            new OcrLine(0, 0, Rectangle.Union(first.BoundsPx, second.BoundsPx), [first, second]),
            new OcrLine(1, 1, third.BoundsPx, [third]),
        ]);

        var range = TextSelectionRange.Create(document, first, third);

        Assert.Equal($"Hello, world{Environment.NewLine}世界", range.Text);
        Assert.Equal(Rectangle.FromLTRB(1, 1, 44, 30), range.BoundsPx);
    }

    [Fact]
    public void Reverse_drag_returns_the_same_range()
    {
        var first = new OcrWord(0, 0, 0, "one", new Rectangle(0, 0, 10, 10));
        var second = new OcrWord(1, 0, 1, "two", new Rectangle(12, 0, 10, 10));
        var document = new OcrDocument("en", new Size(30, 20),
            [new OcrLine(0, 0, Rectangle.Union(first.BoundsPx, second.BoundsPx), [first, second])]);

        Assert.Equal("one two", TextSelectionRange.Create(document, second, first).Text);
    }
}
