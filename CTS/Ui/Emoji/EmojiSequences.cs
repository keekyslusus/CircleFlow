using System.Text;

namespace CircleToSearch.Ui.Emoji;

internal readonly record struct EmojiTextPart(string Text, string? Key);

// Finds the longest emoji sequence at each position. Keys are lowercase hex code points joined by '_'
// without U+FE0F, so a sequence matches whether or not the text carries its variation selectors.
internal sealed class EmojiSequences
{
    private const int TextSelector = 0xFE0E;
    private const int EmojiSelector = 0xFE0F;

    private readonly HashSet<string> _keys;
    private readonly HashSet<string> _prefixes = new(StringComparer.Ordinal);
    private readonly HashSet<int> _textDefault;

    internal EmojiSequences(IEnumerable<string> keys, IEnumerable<int> textDefault)
    {
        _keys = new HashSet<string>(keys, StringComparer.Ordinal);
        _textDefault = [.. textDefault];
        foreach (var key in _keys)
            for (var end = key.IndexOf('_'); end >= 0; end = key.IndexOf('_', end + 1))
                _prefixes.Add(key[..end]);
    }

    internal static EmojiSequences Empty { get; } = new([], []);

    // Like Android, a text-default symbol without U+FE0F stays text only when the text font draws it,
    // so (c) and the heart suit remain text while a lone U+2764 still becomes a red heart.
    internal IReadOnlyList<EmojiTextPart> Split(string text, Func<int, bool> hasTextGlyph)
    {
        var parts = new List<EmojiTextPart>();
        var textStart = 0;
        for (var index = 0; index < text.Length;)
        {
            var (key, length) = Match(text, index, hasTextGlyph);
            if (key is null)
            {
                index += char.IsSurrogatePair(text, index) ? 2 : 1;
                continue;
            }
            if (index > textStart) parts.Add(new(text[textStart..index], null));
            parts.Add(new(text.Substring(index, length), key));
            index += length;
            textStart = index;
        }
        if (textStart < text.Length) parts.Add(new(text[textStart..], null));
        return parts;
    }

    private (string? Key, int Length) Match(string text, int start, Func<int, bool> hasTextGlyph)
    {
        if (_keys.Count == 0) return (null, 0);
        var key = new StringBuilder();
        string? matched = null;
        var matchedEnd = start;
        var components = 0;
        var first = 0;
        for (var index = start; index < text.Length;)
        {
            if (char.IsSurrogate(text, index) && !char.IsSurrogatePair(text, index)) break;
            var codePoint = char.ConvertToUtf32(text, index);
            index += char.IsSurrogatePair(text, index) ? 2 : 1;
            if (codePoint == EmojiSelector && components > 0) continue;
            if (components == 0) first = codePoint;
            if (components > 0) key.Append('_');
            key.Append(codePoint.ToString("x"));
            components++;
            var candidate = key.ToString();
            if (_keys.Contains(candidate))
            {
                var end = index;
                var next = end < text.Length ? text[end] : '\0';
                if (next == EmojiSelector) end++;
                var allowed = components > 1 ||
                    (next != TextSelector &&
                     (next == EmojiSelector || !_textDefault.Contains(first) || !hasTextGlyph(first)));
                if (allowed)
                {
                    matched = candidate;
                    matchedEnd = end;
                }
            }
            if (!_prefixes.Contains(candidate)) break;
        }
        return (matched, matchedEnd - start);
    }
}
