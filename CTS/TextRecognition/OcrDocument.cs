using System.Collections.ObjectModel;
using System.Drawing;

namespace CircleToSearch.TextRecognition;

public sealed record OcrDocument
{
    private readonly IReadOnlyList<OcrWord> _words;

    public OcrDocument(string languageTag, Size pixelSize, IReadOnlyList<OcrLine> lines)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageTag);
        if (pixelSize.Width <= 0 || pixelSize.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelSize));
        LanguageTag = languageTag;
        PixelSize = pixelSize;
        Lines = new ReadOnlyCollection<OcrLine>((lines ?? throw new ArgumentNullException(nameof(lines))).ToArray());
        _words = new ReadOnlyCollection<OcrWord>(Lines.SelectMany(line => line.Words)
            .OrderBy(word => word.ReadingOrder).ToArray());
    }

    public string LanguageTag { get; }
    public Size PixelSize { get; }
    public IReadOnlyList<OcrLine> Lines { get; }
    public IReadOnlyList<OcrWord> Words => _words;
}

public sealed record OcrLine
{
    public OcrLine(int id, int order, string languageTag, Rectangle boundsPx, IReadOnlyList<OcrWord> words)
    {
        if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (order < 0) throw new ArgumentOutOfRangeException(nameof(order));
        ArgumentException.ThrowIfNullOrWhiteSpace(languageTag);
        if (boundsPx.Width <= 0 || boundsPx.Height <= 0) throw new ArgumentOutOfRangeException(nameof(boundsPx));
        Id = id;
        Order = order;
        LanguageTag = languageTag;
        BoundsPx = boundsPx;
        Words = new ReadOnlyCollection<OcrWord>((words ?? throw new ArgumentNullException(nameof(words))).ToArray());
    }

    public int Id { get; }
    public int Order { get; }
    public string LanguageTag { get; }
    public Rectangle BoundsPx { get; }
    public IReadOnlyList<OcrWord> Words { get; }
    public string Text => string.Join(' ', Words.OrderBy(word => word.ReadingOrder).Select(word => word.Text));
}

public sealed record OcrWord
{
    public OcrWord(int id, int lineId, int readingOrder, string languageTag, string text, Rectangle boundsPx)
    {
        if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (lineId < 0) throw new ArgumentOutOfRangeException(nameof(lineId));
        if (readingOrder < 0) throw new ArgumentOutOfRangeException(nameof(readingOrder));
        ArgumentException.ThrowIfNullOrWhiteSpace(languageTag);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (boundsPx.Width <= 0 || boundsPx.Height <= 0) throw new ArgumentOutOfRangeException(nameof(boundsPx));
        Id = id;
        LineId = lineId;
        ReadingOrder = readingOrder;
        LanguageTag = languageTag;
        Text = text;
        BoundsPx = boundsPx;
    }

    public int Id { get; }
    public int LineId { get; }
    public int ReadingOrder { get; }
    public string LanguageTag { get; }
    public string Text { get; }
    public Rectangle BoundsPx { get; }
}
