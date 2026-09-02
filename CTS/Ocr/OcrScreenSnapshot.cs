namespace CircleToSearch.Ocr;

using System.Windows;

public sealed class OcrScreenSnapshot
{
    public static OcrScreenSnapshot Empty { get; } = new([], [], string.Empty);

    public IReadOnlyList<OcrLineSnapshot> Lines { get; }
    public IReadOnlyList<OcrWordSnapshot> Words { get; }
    public string FullText { get; }

    public OcrScreenSnapshot(
        IReadOnlyList<OcrLineSnapshot> lines,
        IReadOnlyList<OcrWordSnapshot> words,
        string fullText)
    {
        Lines = lines ?? throw new ArgumentNullException(nameof(lines));
        Words = words ?? throw new ArgumentNullException(nameof(words));
        FullText = fullText ?? string.Empty;
    }

    public OcrWordSnapshot? FindWordAt(Point dipPoint)
    {
        for (var i = 0; i < Words.Count; i++)
        {
            var word = Words[i];
            if (word.DipRect.Contains(dipPoint))
                return word;
        }
        return null;
    }

    public IReadOnlyList<OcrWordSnapshot> FindWordsInRange(Point startDip, Point endDip)
    {
        if (Words.Count == 0) return [];

        var startWord = FindClosestWord(startDip);
        var endWord = FindClosestWord(endDip);
        if (startWord is null || endWord is null) return [];

        var startIndex = -1;
        var endIndex = -1;
        for (var i = 0; i < Words.Count; i++)
        {
            if (ReferenceEquals(Words[i], startWord)) startIndex = i;
            if (ReferenceEquals(Words[i], endWord)) endIndex = i;
        }

        if (startIndex == -1 || endIndex == -1) return [];

        var from = Math.Min(startIndex, endIndex);
        var to = Math.Max(startIndex, endIndex);
        var selected = new List<OcrWordSnapshot>(to - from + 1);
        for (var i = from; i <= to; i++)
        {
            selected.Add(Words[i]);
        }
        return selected;
    }

    public OcrWordSnapshot? FindClosestWord(Point dipPoint)
    {
        OcrWordSnapshot? best = null;
        var bestDistanceSq = double.MaxValue;
        for (var i = 0; i < Words.Count; i++)
        {
            var word = Words[i];
            if (word.DipRect.Contains(dipPoint)) return word;

            var cx = word.DipRect.X + word.DipRect.Width / 2;
            var cy = word.DipRect.Y + word.DipRect.Height / 2;
            var dx = cx - dipPoint.X;
            var dy = cy - dipPoint.Y;
            var distSq = dx * dx + dy * dy;
            if (distSq < bestDistanceSq)
            {
                bestDistanceSq = distSq;
                best = word;
            }
        }
        return best;
    }
}
