using System.Globalization;
using System.Text;

namespace CircleToSearch.TextRecognition;

public sealed record OcrTextQuality(
    double Score,
    int LetterCount,
    double ScriptConcentration,
    double ProfileRatio,
    bool IsUseful,
    OcrUnicodeScriptAnalysis Scripts);

public sealed class OcrTextQualityScorer(OcrUnicodeScriptClassifier scriptClassifier)
{
    private static readonly IReadOnlyDictionary<string, LanguageProfile> Profiles =
        new Dictionary<string, LanguageProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["ru"] = new(
                OcrUnicodeScript.Cyrillic,
                Bigrams("ст но то на ен ов ни ра во ко ро по пр ос го ал ли ер ре от та ан ор те ка ла ве ит ар ет ол од ль ть ил ый ие за ск не ва ти се ри")),
            ["en"] = new(
                OcrUnicodeScript.Latin,
                Bigrams("th he in er an re on at en nd ti es or te of ed is it al ar st to nt ng se ha as ou io le ve co me de hi ri ro ic ne ea ra ce li ch ll be ma si om ur ad")),
        };

    public OcrTextQuality Score(string text, string languageTag)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(languageTag);
        var runes = text.EnumerateRunes().ToArray();
        var scripts = scriptClassifier.Analyze(text);
        var bad = runes.Count(rune => Rune.GetUnicodeCategory(rune) is
            UnicodeCategory.Control or UnicodeCategory.PrivateUse or UnicodeCategory.Surrogate || rune.Value == 0xfffd);
        var useful = runes.Count(rune => !Rune.IsWhiteSpace(rune) && !Rune.IsPunctuation(rune));
        var visible = runes.Count(rune => !Rune.IsWhiteSpace(rune));
        var mixedTokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Count(token => token.Any(char.IsLetter) && token.Any(char.IsDigit));
        var uppercaseTransitions = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Sum(UppercaseTransitions);
        var scriptConcentration = scripts.LetterCount == 0
            ? 0
            : scripts.LetterCounts.Values.Max() / (double)scripts.LetterCount;
        var profile = GetProfile(languageTag);
        var profileRatio = profile is null ? 0 : CalculateBigramRatio(text, profile.Bigrams);
        var expectedScriptRatio = profile is null || scripts.LetterCount == 0
            ? 0
            : scripts.Count(profile.Script) / (double)scripts.LetterCount;
        var significantScriptPenalty = Math.Max(0, scripts.SignificantScripts.Count - 1) * 1.5;
        var score = useful + scripts.LetterCount * 1.5 + scriptConcentration * 5 +
            expectedScriptRatio * 7 + profileRatio * 14 - significantScriptPenalty -
            bad * 25 - mixedTokens * 2.5 - uppercaseTransitions * 1.5;
        return new OcrTextQuality(
            score,
            scripts.LetterCount,
            scriptConcentration,
            profileRatio,
            visible > 0 && bad <= Math.Max(1, runes.Length / 5),
            scripts);
    }

    internal OcrLine Choose(IReadOnlyList<OcrLine> candidates, IReadOnlyList<string> languageOrder)
    {
        if (candidates.Count == 0) throw new ArgumentException("At least one candidate is required.", nameof(candidates));
        var distinct = candidates.GroupBy(line => Normalize(line.Text), StringComparer.Ordinal)
            .Select(group => ChooseEquivalent(group.ToArray(), languageOrder)).ToArray();
        if (distinct.Length == 1) return distinct[0];
        var scored = distinct.Select(line => (Line: line, Quality: Score(line.Text, line.LanguageTag))).ToArray();

        var russian = scored.Where(item => IsLanguage(item.Line.LanguageTag, "ru") &&
                                           item.Quality.Scripts.DominantScript == OcrUnicodeScript.Cyrillic &&
                                           item.Quality.ProfileRatio >= 0.15 && item.Quality.IsUseful)
            .OrderByDescending(item => item.Quality.Score).FirstOrDefault();
        if (russian.Line is not null)
        {
            var alternatives = scored.Where(item => !ReferenceEquals(item.Line, russian.Line)).ToArray();
            var looksLikeMojibake = russian.Quality.LetterCount >= 4 &&
                alternatives.Any(item => item.Quality.Scripts.Count(OcrUnicodeScript.Cyrillic) == 0 &&
                    (item.Quality.Scripts.Count(OcrUnicodeScript.Latin) > item.Quality.LetterCount * 0.65 ||
                     item.Line.Text.Any(char.IsDigit)) &&
                    item.Quality.Score <= russian.Quality.Score + 8);
            if (looksLikeMojibake) return russian.Line;
        }

        return scored.OrderByDescending(item => item.Quality.Score)
            .ThenBy(item => LanguageIndex(item.Line.LanguageTag, languageOrder))
            .ThenBy(item => item.Line.LanguageTag, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Line.LanguageTag, StringComparer.Ordinal)
            .Select(item => item.Line)
            .First();
    }

    private OcrLine ChooseEquivalent(IReadOnlyList<OcrLine> candidates, IReadOnlyList<string> languageOrder) =>
        candidates.Select(line => (Line: line, Quality: Score(line.Text, line.LanguageTag)))
            .OrderByDescending(item => item.Quality.Score)
            .ThenBy(item => LanguageIndex(item.Line.LanguageTag, languageOrder))
            .ThenBy(item => item.Line.LanguageTag, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Line.LanguageTag, StringComparer.Ordinal)
            .Select(item => item.Line)
            .First();

    private static int UppercaseTransitions(string token)
    {
        var transitions = 0;
        for (var index = 1; index < token.Length; index++)
            if (char.IsUpper(token[index]) && char.IsLower(token[index - 1])) transitions++;
        return transitions;
    }

    private static double CalculateBigramRatio(string text, IReadOnlySet<string> profile)
    {
        var letters = text.EnumerateRunes().Where(Rune.IsLetter)
            .Select(rune => rune.ToString().ToLowerInvariant()).ToArray();
        if (letters.Length < 2) return 0;
        var pairs = Enumerable.Range(0, letters.Length - 1)
            .Select(index => letters[index] + letters[index + 1]).ToArray();
        return pairs.Count(profile.Contains) / (double)pairs.Length;
    }

    private static LanguageProfile? GetProfile(string languageTag)
    {
        var separator = languageTag.IndexOf('-');
        var neutral = separator < 0 ? languageTag : languageTag[..separator];
        return Profiles.GetValueOrDefault(neutral);
    }

    private static bool IsLanguage(string languageTag, string neutralTag) =>
        string.Equals(languageTag, neutralTag, StringComparison.OrdinalIgnoreCase) ||
        languageTag.StartsWith(neutralTag + "-", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static int LanguageIndex(string tag, IReadOnlyList<string> order)
    {
        for (var index = 0; index < order.Count; index++)
            if (string.Equals(tag, order[index], StringComparison.OrdinalIgnoreCase)) return index;
        return int.MaxValue;
    }

    private static HashSet<string> Bigrams(string value) =>
        value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

    private sealed record LanguageProfile(OcrUnicodeScript Script, IReadOnlySet<string> Bigrams);
}
