using System.Collections.Frozen;
using System.Text;

namespace CircleToSearch.TextRecognition;

public enum OcrUnicodeScript
{
    Latin,
    Cyrillic,
    Greek,
    Arabic,
    Hebrew,
    Devanagari,
    Han,
    HiraganaKatakana,
    Hangul,
    Thai,
    OtherLetter,
}

public sealed record OcrUnicodeScriptAnalysis(
    int LetterCount,
    IReadOnlyDictionary<OcrUnicodeScript, int> LetterCounts,
    OcrUnicodeScript? DominantScript,
    IReadOnlySet<OcrUnicodeScript> SignificantScripts)
{
    public int Count(OcrUnicodeScript script) => LetterCounts.GetValueOrDefault(script);
}

public sealed class OcrUnicodeScriptClassifier
{
    public OcrUnicodeScriptAnalysis Analyze(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var counts = new Dictionary<OcrUnicodeScript, int>();
        var letterCount = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (!Rune.IsLetter(rune)) continue;
            letterCount++;
            var script = ClassifyLetter(rune);
            counts[script] = counts.GetValueOrDefault(script) + 1;
        }

        var dominant = counts.OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key)
            .Select(item => (OcrUnicodeScript?)item.Key)
            .FirstOrDefault();
        var significant = counts.Where(item => item.Value >= 2 || item.Value >= letterCount * 0.2)
            .Select(item => item.Key)
            .ToFrozenSet();
        return new OcrUnicodeScriptAnalysis(
            letterCount,
            counts.ToFrozenDictionary(),
            dominant,
            significant);
    }

    private static OcrUnicodeScript ClassifyLetter(Rune rune)
    {
        var value = rune.Value;
        if (In(value, 0x0041, 0x005a) || In(value, 0x0061, 0x007a) ||
            In(value, 0x00c0, 0x02e4) || In(value, 0x1d00, 0x1d7f) ||
            In(value, 0x1d80, 0x1dbf) || In(value, 0x1e00, 0x1eff) ||
            In(value, 0x2c60, 0x2c7f) || In(value, 0xa720, 0xa7ff) ||
            In(value, 0xab30, 0xab6f) || In(value, 0xfb00, 0xfb06) ||
            In(value, 0xff21, 0xff3a) || In(value, 0xff41, 0xff5a))
            return OcrUnicodeScript.Latin;
        if (In(value, 0x0370, 0x03ff) || In(value, 0x1f00, 0x1fff))
            return OcrUnicodeScript.Greek;
        if (In(value, 0x0400, 0x052f) || In(value, 0x1c80, 0x1c8f) ||
            In(value, 0x2de0, 0x2dff) || In(value, 0xa640, 0xa69f))
            return OcrUnicodeScript.Cyrillic;
        if (In(value, 0x0590, 0x05ff) || In(value, 0xfb1d, 0xfb4f))
            return OcrUnicodeScript.Hebrew;
        if (In(value, 0x0600, 0x06ff) || In(value, 0x0750, 0x077f) ||
            In(value, 0x08a0, 0x08ff) || In(value, 0xfb50, 0xfdff) ||
            In(value, 0xfe70, 0xfeff) || In(value, 0x1ee00, 0x1eeff))
            return OcrUnicodeScript.Arabic;
        if (In(value, 0x0900, 0x097f) || In(value, 0xa8e0, 0xa8ff) ||
            In(value, 0x11b00, 0x11b5f))
            return OcrUnicodeScript.Devanagari;
        if (In(value, 0x3040, 0x30ff) || In(value, 0x31f0, 0x31ff) ||
            In(value, 0xff66, 0xff9d) || In(value, 0x1b000, 0x1b122) ||
            In(value, 0x1b150, 0x1b152))
            return OcrUnicodeScript.HiraganaKatakana;
        if (In(value, 0x1100, 0x11ff) || In(value, 0x3130, 0x318f) ||
            In(value, 0xa960, 0xa97f) || In(value, 0xac00, 0xd7af) ||
            In(value, 0xd7b0, 0xd7ff))
            return OcrUnicodeScript.Hangul;
        if (In(value, 0x0e00, 0x0e7f))
            return OcrUnicodeScript.Thai;
        if (In(value, 0x3400, 0x4dbf) || In(value, 0x4e00, 0x9fff) ||
            In(value, 0xf900, 0xfaff) || In(value, 0x20000, 0x2ebef) ||
            In(value, 0x30000, 0x323af))
            return OcrUnicodeScript.Han;
        return OcrUnicodeScript.OtherLetter;
    }

    private static bool In(int value, int start, int end) => value >= start && value <= end;
}
