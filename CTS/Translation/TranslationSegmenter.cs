using System.Text;

namespace CircleToSearch.Translation;

public sealed class TranslationSegmenter(int maximumUtf8Bytes = 500)
{
    public int MaximumUtf8Bytes { get; } = maximumUtf8Bytes > 0
        ? maximumUtf8Bytes
        : throw new ArgumentOutOfRangeException(nameof(maximumUtf8Bytes));

    public IReadOnlyList<TranslationChunk> Segment(int lineId, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var chunks = new List<TranslationChunk>();
        var remaining = text;
        var order = 0;
        while (remaining.Length > 0)
        {
            var length = LargestPrefix(remaining, MaximumUtf8Bytes);
            if (length == remaining.Length)
            {
                chunks.Add(new TranslationChunk(lineId, order, remaining));
                break;
            }
            var split = FindNaturalSplit(remaining, length);
            var piece = remaining[..split];
            chunks.Add(new TranslationChunk(lineId, order++, piece));
            remaining = remaining[split..];
        }
        return chunks;
    }

    private static int LargestPrefix(string text, int maximumBytes)
    {
        var bytes = 0;
        var length = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var runeBytes = rune.Utf8SequenceLength;
            if (bytes + runeBytes > maximumBytes) break;
            bytes += runeBytes;
            length += rune.Utf16SequenceLength;
        }
        if (length == 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes), "The byte limit cannot contain one Unicode scalar value.");
        return length;
    }

    private static int FindNaturalSplit(string text, int maximumLength)
    {
        for (var index = maximumLength; index > 0; index--)
        {
            var character = text[index - 1];
            if (char.IsWhiteSpace(character) || character is '.' or '!' or '?' or ';' or ':' or '。' or '！' or '？')
                return index;
        }
        return maximumLength;
    }
}
