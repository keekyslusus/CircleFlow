using System.Drawing;

namespace CircleToSearch.TextRecognition;

public sealed record TextSelectionRange
{
    private TextSelectionRange(IReadOnlyList<OcrWord> words, Rectangle boundsPx, string text) =>
        (Words, BoundsPx, Text) = (words, boundsPx, text);

    public IReadOnlyList<OcrWord> Words { get; }
    public IReadOnlyList<Rectangle> HighlightBoundsPx => Words.Select(word => word.BoundsPx).ToArray();
    public Rectangle BoundsPx { get; }
    public string Text { get; }

    public static TextSelectionRange Create(OcrDocument document, OcrWord anchor, OcrWord current)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(current);
        if (!document.Words.Contains(anchor) || !document.Words.Contains(current))
            throw new ArgumentException("The selected words are not part of the document.");
        var minimum = Math.Min(anchor.ReadingOrder, current.ReadingOrder);
        var maximum = Math.Max(anchor.ReadingOrder, current.ReadingOrder);
        var selected = document.Words
            .Where(word => word.ReadingOrder >= minimum && word.ReadingOrder <= maximum)
            .OrderBy(word => word.ReadingOrder)
            .ToArray();
        if (selected.Length == 0) throw new ArgumentException("The selected words are not part of the document.");

        var lineOrder = document.Lines.ToDictionary(line => line.Id, line => line.Order);
        var text = string.Join(Environment.NewLine, selected
            .GroupBy(word => word.LineId)
            .OrderBy(group => lineOrder.GetValueOrDefault(group.Key, int.MaxValue))
            .Select(group => string.Join(' ', group.OrderBy(word => word.ReadingOrder).Select(word => word.Text))));
        var bounds = selected.Select(word => word.BoundsPx).Aggregate(Rectangle.Union);
        return new TextSelectionRange(selected, bounds, text);
    }
}
