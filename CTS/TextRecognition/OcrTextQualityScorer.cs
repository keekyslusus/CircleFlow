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
        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var mixedTokens = tokens.Count(token => token.Any(char.IsLetter) && token.Any(char.IsDigit));
        var uppercaseTransitions = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Sum(UppercaseTransitions);
        var scriptConcentration = scripts.LetterCount == 0
            ? 0
            : scripts.LetterCounts.Values.Max() / (double)scripts.LetterCount;
        var profile = GetProfile(languageTag);
        var profileRatio = profile is null ? 0 : CalculateBigramRatio(text, profile.Bigrams);
        var significantScriptPenalty = Math.Max(0, scripts.SignificantScripts.Count - 1) * 1.5;
        var usefulRatio = visible == 0 ? 0 : useful / (double)visible;
        var badRatio = runes.Length == 0 ? 0 : bad / (double)runes.Length;
        var mixedTokenRatio = mixedTokens / (double)Math.Max(1, tokens.Length);
        var uppercaseTransitionRatio = uppercaseTransitions / (double)Math.Max(1, scripts.LetterCount);
        var score = (visible > 0 ? 10 : 0) + usefulRatio * 10 + scriptConcentration * 5 -
            significantScriptPenalty - badRatio * 25 - mixedTokenRatio * 4 - uppercaseTransitionRatio * 6;
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
        var informationPreserving = distinct.GroupBy(line => FoldForComparison(line.Text), StringComparer.Ordinal)
            .Select(group => ChooseInformationPreserving(group.ToArray(), languageOrder)).ToArray();
        if (informationPreserving.Length == 1) return informationPreserving[0];
        var scored = informationPreserving.Select(line => (Line: line, Quality: Score(line.Text, line.LanguageTag))).ToArray();

        var protectedCandidate = ChooseMojibakeProtectedCandidate(scored);
        if (protectedCandidate is not null) return protectedCandidate;

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

    private OcrLine ChooseInformationPreserving(
        IReadOnlyList<OcrLine> candidates,
        IReadOnlyList<string> languageOrder) => candidates
            .Select(line => (Line: line, Information: PreservedUnicodeLetterCount(line.Text),
                Quality: Score(line.Text, line.LanguageTag)))
            .OrderByDescending(item => item.Information)
            .ThenByDescending(item => item.Quality.Score)
            .ThenBy(item => LanguageIndex(item.Line.LanguageTag, languageOrder))
            .ThenBy(item => item.Line.LanguageTag, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Line.LanguageTag, StringComparer.Ordinal)
            .Select(item => item.Line)
            .First();

    private static OcrLine? ChooseMojibakeProtectedCandidate(
        IReadOnlyList<(OcrLine Line, OcrTextQuality Quality)> candidates)
    {
        var russian = candidates.Where(item => GetProfile(item.Line.LanguageTag)?.Script == OcrUnicodeScript.Cyrillic &&
                                                item.Quality.Scripts.DominantScript == OcrUnicodeScript.Cyrillic &&
                                                item.Quality.ProfileRatio >= 0.15 && item.Quality.IsUseful &&
                                                !HasMixedAlphanumericToken(item.Line.Text))
            .OrderByDescending(item => item.Quality.Score).FirstOrDefault();
        if (russian.Line is not null)
        {
            var alternatives = candidates.Where(item => !ReferenceEquals(item.Line, russian.Line)).ToArray();
            var looksLikeMojibake = russian.Quality.LetterCount >= 4 &&
                alternatives.Any(item => item.Quality.Scripts.Count(OcrUnicodeScript.Cyrillic) == 0 &&
                    (item.Quality.Scripts.Count(OcrUnicodeScript.Latin) > item.Quality.LetterCount * 0.65 ||
                     item.Line.Text.Any(char.IsDigit)) &&
                    item.Quality.Score <= russian.Quality.Score + 8);
            if (looksLikeMojibake) return russian.Line;
        }

        if (candidates.Any(item => GetProfile(item.Line.LanguageTag) is null)) return null;
        var compatible = candidates.Where(item =>
        {
            var profile = GetProfile(item.Line.LanguageTag)!;
            return item.Quality.IsUseful && item.Quality.Scripts.DominantScript == profile.Script;
        }).OrderByDescending(item => item.Quality.ProfileRatio)
          .ThenByDescending(item => item.Quality.Score)
          .ToArray();
        if (compatible.Length == 0 || compatible[0].Quality.ProfileRatio < 0.15) return null;
        var runnerUpRatio = compatible.Length > 1 ? compatible[1].Quality.ProfileRatio : 0;
        return compatible[0].Quality.ProfileRatio >= runnerUpRatio + 0.1 ? compatible[0].Line : null;
    }

    private static bool HasMixedAlphanumericToken(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Any(token => token.Any(char.IsLetter) && token.Any(char.IsDigit));

    private static int PreservedUnicodeLetterCount(string text) => text.EnumerateRunes().Count(rune =>
        rune.Value > 0x7f && (Rune.IsLetter(rune) || Rune.GetUnicodeCategory(rune) is
            UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark));

    private static string FoldForComparison(string text)
    {
        var builder = new StringBuilder();
        foreach (var rune in Normalize(text).Normalize(NormalizationForm.FormKD).EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or
                UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                continue;
            var expansion = rune.Value switch
            {
                0x00df or 0x1e9e => "ss",
                0x00c6 or 0x00e6 => "ae",
                0x0152 or 0x0153 => "oe",
                0x00d0 or 0x00f0 or 0x0110 or 0x0111 => "d",
                0x0126 or 0x0127 => "h",
                0x0131 => "i",
                0x0141 or 0x0142 => "l",
                0x00d8 or 0x00f8 => "o",
                0x00de or 0x00fe => "th",
                _ => rune.ToString().ToLowerInvariant(),
            };
            builder.Append(expansion);
        }
        return builder.ToString();
    }

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
