using System.Drawing;

namespace CircleToSearch.TextRecognition;

public sealed record TextSelectionRange
{
    private TextSelectionRange(
        IReadOnlyList<OcrWord> words,
        IReadOnlyList<TextSelectionLine> lines,
        Rectangle boundsPx,
        string text) =>
        (Words, Lines, BoundsPx, Text) = (words, lines, boundsPx, text);

    public IReadOnlyList<OcrWord> Words { get; }
    public IReadOnlyList<TextSelectionLine> Lines { get; }
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

        var documentLines = document.Lines.ToDictionary(line => line.Id);
        var lines = selected
            .GroupBy(word => word.LineId)
            .OrderBy(group => documentLines.GetValueOrDefault(group.Key)?.Order ?? int.MaxValue)
            .Select(group =>
            {
                var words = group.OrderBy(word => word.ReadingOrder).ToArray();
                var sourceLine = documentLines[group.Key];
                return new TextSelectionLine(
                    sourceLine.Id,
                    sourceLine.Order,
                    sourceLine.LanguageTag,
                    words,
                    words.Select(word => word.BoundsPx).Aggregate(Rectangle.Union),
                    string.Join(' ', words.Select(word => word.Text)));
            }).ToArray();
        var text = string.Join(Environment.NewLine, lines.Select(line => line.PreliminaryText));
        var bounds = selected.Select(word => word.BoundsPx).Aggregate(Rectangle.Union);
        return new TextSelectionRange(selected, lines, bounds, text);
    }
}

public sealed record TextSelectionLine(
    int LineId,
    int Order,
    string LanguageTag,
    IReadOnlyList<OcrWord> Words,
    Rectangle BoundsPx,
    string PreliminaryText);
