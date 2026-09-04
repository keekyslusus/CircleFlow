using System.Globalization;
using System.Text;

namespace CircleToSearch.TextRecognition;

public sealed record OcrTextQuality(
    double Score,
    int LetterCount,
    double CyrillicRatio,
    double LatinRatio,
    double BigramRatio,
    bool IsNativeRussian,
    bool IsUseful);

public sealed class OcrTextQualityScorer
{
    private static readonly HashSet<string> RussianBigrams = Bigrams(
        "ст но то на ен ов ни ра во ко ро по пр ос го ал ли ер ре от та ан ор те ка ла ве ит ар ет ол од ль ть ил ый ие за ск не ва ти се ри");
    private static readonly HashSet<string> EnglishBigrams = Bigrams(
        "th he in er an re on at en nd ti es or te of ed is it al ar st to nt ng se ha as ou io le ve co me de hi ri ro ic ne ea ra ce li ch ll be ma si om ur");

    public OcrTextQuality Score(string text, string languageTag)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(languageTag);
        var runes = text.EnumerateRunes().ToArray();
        var letters = runes.Where(Rune.IsLetter).ToArray();
        var cyrillic = letters.Count(IsCyrillic);
        var latin = letters.Count(IsLatin);
        var bad = runes.Count(rune => Rune.GetUnicodeCategory(rune) is
            UnicodeCategory.Control or UnicodeCategory.PrivateUse or UnicodeCategory.Surrogate || rune.Value == 0xfffd);
        var useful = runes.Count(rune => !Rune.IsWhiteSpace(rune) && !Rune.IsPunctuation(rune));
        var visible = runes.Count(rune => !Rune.IsWhiteSpace(rune));
        var mixedTokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Count(token => token.Any(char.IsLetter) && token.Any(char.IsDigit));
        var uppercaseTransitions = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Sum(UppercaseTransitions);
        var profile = languageTag.StartsWith("ru", StringComparison.OrdinalIgnoreCase)
            ? RussianBigrams
            : EnglishBigrams;
        var normalizedLetters = string.Concat(letters.Select(rune => rune.ToString())).ToLowerInvariant();
        var pairs = Enumerable.Range(0, Math.Max(0, normalizedLetters.Length - 1))
            .Select(index => normalizedLetters.Substring(index, 2))
            .Where(pair => pair.All(char.IsLetter))
            .ToArray();
        var bigramRatio = pairs.Length == 0 ? 0 : pairs.Count(profile.Contains) / (double)pairs.Length;
        var letterCount = letters.Length;
        var cyrillicRatio = letterCount == 0 ? 0 : cyrillic / (double)letterCount;
        var latinRatio = letterCount == 0 ? 0 : latin / (double)letterCount;
        var expectedScriptRatio = languageTag.StartsWith("ru", StringComparison.OrdinalIgnoreCase)
            ? cyrillicRatio
            : latinRatio;
        var score = useful + letterCount * 1.5 + expectedScriptRatio * 12 + bigramRatio * 14
            - bad * 25 - mixedTokens * 2.5 - uppercaseTransitions * 1.5;
        return new OcrTextQuality(
            score,
            letterCount,
            cyrillicRatio,
            latinRatio,
            bigramRatio,
            cyrillic >= 2 && cyrillicRatio >= 0.5,
            visible > 0 && bad <= Math.Max(1, runes.Length / 5));
    }

    internal OcrLine Choose(IReadOnlyList<OcrLine> candidates, IReadOnlyList<string> languageOrder)
    {
        if (candidates.Count == 0) throw new ArgumentException("At least one candidate is required.", nameof(candidates));
        var distinct = candidates.GroupBy(line => Normalize(line.Text), StringComparer.Ordinal)
            .Select(group => ChooseEquivalent(group.ToArray(), languageOrder)).ToArray();
        if (distinct.Length == 1) return distinct[0];
        var scored = distinct.Select(line => (Line: line, Quality: Score(line.Text, line.LanguageTag))).ToArray();
        var russian = scored.Where(item => item.Line.LanguageTag.StartsWith("ru", StringComparison.OrdinalIgnoreCase) &&
                                           item.Quality.IsNativeRussian && item.Quality.IsUseful)
            .OrderByDescending(item => item.Quality.Score).FirstOrDefault();
        if (russian.Line is not null)
        {
            var alternatives = scored.Where(item => !ReferenceEquals(item.Line, russian.Line)).ToArray();
            var looksLikeMojibake = russian.Quality.LetterCount >= 4 && russian.Quality.BigramRatio >= 0.15 &&
                alternatives.Any(item => item.Quality.CyrillicRatio == 0 &&
                (item.Quality.LatinRatio > 0.65 || item.Line.Text.Any(char.IsDigit)) &&
                item.Quality.Score <= russian.Quality.Score + 8);
            if (looksLikeMojibake) return russian.Line;
        }
        var english = scored.Where(item => item.Line.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase) &&
                                           item.Quality.LatinRatio >= 0.5 && item.Quality.IsUseful)
            .OrderByDescending(item => item.Quality.Score).FirstOrDefault();
        var best = scored.OrderByDescending(item => item.Quality.Score)
            .ThenBy(item => LanguageIndex(item.Line.LanguageTag, languageOrder))
            .First();
        if (english.Line is not null && english.Quality.Score >= best.Quality.Score - 3) return english.Line;
        return best.Line;
    }

    private static int UppercaseTransitions(string token)
    {
        var transitions = 0;
        for (var index = 1; index < token.Length; index++)
            if (char.IsUpper(token[index]) && char.IsLower(token[index - 1])) transitions++;
        return transitions;
    }

    private OcrLine ChooseEquivalent(IReadOnlyList<OcrLine> candidates, IReadOnlyList<string> languageOrder)
    {
        var quality = Score(candidates[0].Text, candidates[0].LanguageTag);
        if (quality.CyrillicRatio >= 0.5)
        {
            var russian = candidates.FirstOrDefault(line => line.LanguageTag.StartsWith("ru", StringComparison.OrdinalIgnoreCase));
            if (russian is not null) return russian;
        }
        if (quality.LatinRatio >= 0.5)
        {
            var english = candidates.FirstOrDefault(line => line.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase));
            if (english is not null) return english;
        }
        return candidates.OrderBy(line => LanguageIndex(line.LanguageTag, languageOrder)).First();
    }

    private static bool IsCyrillic(Rune rune) => rune.Value is >= 0x0400 and <= 0x052f;
    private static bool IsLatin(Rune rune) => rune.Value is >= 0x0041 and <= 0x024f;
    private static string Normalize(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static int LanguageIndex(string tag, IReadOnlyList<string> order)
    {
        for (var index = 0; index < order.Count; index++)
            if (string.Equals(tag, order[index], StringComparison.OrdinalIgnoreCase)) return index;
        return int.MaxValue;
    }
    private static HashSet<string> Bigrams(string value) => value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
}
