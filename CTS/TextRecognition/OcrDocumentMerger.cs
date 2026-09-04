using System.Drawing;

namespace CircleToSearch.TextRecognition;

public sealed class OcrDocumentMerger(OcrTextQualityScorer scorer)
{
    public OcrDocument Merge(IReadOnlyList<OcrDocument> documents, IReadOnlyList<string> languageOrder)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(languageOrder);
        if (documents.Count == 0) throw new ArgumentException("At least one document is required.", nameof(documents));
        var size = documents[0].PixelSize;
        if (documents.Any(document => document.PixelSize != size))
            throw new ArgumentException("OCR documents must use the same pixel size.", nameof(documents));

        var candidates = documents.SelectMany(document => document.Lines)
            .Where(line => scorer.Score(line.Text, line.LanguageTag).IsUseful)
            .OrderBy(line => line.BoundsPx.Top)
            .ThenBy(line => line.BoundsPx.Left)
            .ThenBy(line => LanguageIndex(line.LanguageTag, languageOrder))
            .ToArray();
        var spatialGroups = BuildSpatialGroups(candidates);
        var mergedCandidates = spatialGroups.Select(group => MergeSpatialGroup(group, languageOrder))
            .OrderBy(line => line.Bounds.Top)
            .ThenBy(line => line.Bounds.Left)
            .ToArray();

        var lines = new List<OcrLine>(mergedCandidates.Length);
        var wordId = 0;
        var readingOrder = 0;
        for (var lineId = 0; lineId < mergedCandidates.Length; lineId++)
        {
            var source = mergedCandidates[lineId];
            var words = source.Words.OrderBy(word => word.BoundsPx.Left)
                .Select(word => new OcrWord(
                    wordId++, lineId, readingOrder++, word.LanguageTag, word.Text, word.BoundsPx))
                .ToArray();
            lines.Add(new OcrLine(lineId, lineId, source.LanguageTag, source.Bounds, words));
        }

        var dominant = languageOrder.FirstOrDefault() ?? documents[0].LanguageTag;
        var totals = lines.SelectMany(line => line.Words)
            .GroupBy(word => word.LanguageTag, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Tag = group.First().LanguageTag,
                Letters = group.Sum(word => word.Text.Count(char.IsLetter)),
                Order = LanguageIndex(group.First().LanguageTag, languageOrder),
            })
            .OrderByDescending(item => item.Letters)
            .ThenBy(item => item.Order)
            .FirstOrDefault();
        if (totals is not null) dominant = totals.Tag;
        return new OcrDocument(dominant, size, lines);
    }

    private IReadOnlyList<List<OcrLine>> BuildSpatialGroups(IReadOnlyList<OcrLine> candidates)
    {
        var groups = new List<List<OcrLine>>();
        foreach (var candidate in candidates)
        {
            var group = groups.FirstOrDefault(existing => JoinsSpatialGroup(existing, candidate));
            if (group is null) groups.Add([candidate]);
            else group.Add(candidate);
        }
        return groups;
    }

    private MergedLine MergeSpatialGroup(IReadOnlyList<OcrLine> candidates, IReadOnlyList<string> languageOrder)
    {
        var normalized = candidates.Select(line => Normalize(line.Text)).Distinct(StringComparer.Ordinal).ToArray();
        if (normalized.Length == 1)
        {
            var chosen = scorer.Choose(candidates, languageOrder);
            return new MergedLine(chosen.LanguageTag, chosen.BoundsPx, chosen.Words);
        }

        var wordComponents = BuildWordComponents(candidates.SelectMany(line => line.Words).ToArray(), languageOrder);
        var selected = wordComponents.SelectMany(component => ChooseWords(component, languageOrder))
            .OrderBy(word => word.BoundsPx.Left)
            .ThenBy(word => word.BoundsPx.Top)
            .ToArray();
        var languageTag = DominantLanguage(selected, languageOrder, candidates[0].LanguageTag);
        var bounds = selected.Select(word => word.BoundsPx).Aggregate(Rectangle.Union);
        return new MergedLine(languageTag, bounds, selected);
    }

    private static IReadOnlyList<List<OcrWord>> BuildWordComponents(
        IReadOnlyList<OcrWord> words,
        IReadOnlyList<string> languageOrder)
    {
        var components = new List<List<OcrWord>>();
        foreach (var word in words.OrderBy(word => word.BoundsPx.Left)
                     .ThenBy(word => word.BoundsPx.Top)
                     .ThenBy(word => LanguageIndex(word.LanguageTag, languageOrder)))
        {
            var matches = components.Where(component => component.Any(existing => CanAlign(existing, word))).ToArray();
            if (matches.Length == 0)
            {
                components.Add([word]);
                continue;
            }
            var target = matches[0];
            target.Add(word);
            foreach (var match in matches.Skip(1))
            {
                target.AddRange(match);
                components.Remove(match);
            }
        }
        return components;
    }

    private IReadOnlyList<OcrWord> ChooseWords(IReadOnlyList<OcrWord> candidates, IReadOnlyList<string> languageOrder)
    {
        var alternatives = candidates
            .GroupBy(word => word.LanguageTag, StringComparer.OrdinalIgnoreCase)
            .Select(group => Deduplicate(group.OrderBy(word => word.BoundsPx.Left).ToArray()))
            .ToArray();
        if (alternatives.Length == 1) return alternatives[0];
        var synthetic = alternatives.Select((words, index) => new OcrLine(
            index,
            index,
            words[0].LanguageTag,
            words.Select(word => word.BoundsPx).Aggregate(Rectangle.Union),
            words.Select((word, wordIndex) => new OcrWord(
                wordIndex, index, wordIndex, word.LanguageTag, word.Text, word.BoundsPx)).ToArray()))
            .ToArray();
        var chosen = scorer.Choose(synthetic, languageOrder);
        return alternatives[chosen.Id];
    }

    private static IReadOnlyList<OcrWord> Deduplicate(IReadOnlyList<OcrWord> words)
    {
        var result = new List<OcrWord>(words.Count);
        foreach (var word in words)
        {
            var duplicate = result.Any(existing => Normalize(existing.Text) == Normalize(word.Text) &&
                Coverage(existing.BoundsPx, word.BoundsPx) >= 0.8);
            if (!duplicate) result.Add(word);
        }
        return result;
    }

    private static bool JoinsSpatialGroup(IReadOnlyList<OcrLine> group, OcrLine candidate)
    {
        var anchor = group.OrderByDescending(line => line.BoundsPx.Width).First();
        if (!SameBaseline(anchor.BoundsPx, candidate.BoundsPx)) return false;
        if (HorizontalOverlap(anchor.BoundsPx, candidate.BoundsPx) > 0) return true;
        var gap = HorizontalGap(anchor.BoundsPx, candidate.BoundsPx);
        var maximumHeight = Math.Max(candidate.BoundsPx.Height, anchor.BoundsPx.Height);
        return gap <= maximumHeight;
    }

    private static bool CanAlign(OcrWord first, OcrWord second)
    {
        if (!SameBaseline(first.BoundsPx, second.BoundsPx)) return false;
        var overlap = HorizontalOverlap(first.BoundsPx, second.BoundsPx);
        if (overlap <= 0) return false;
        return overlap >= Math.Min(first.BoundsPx.Width, second.BoundsPx.Width) * 0.2;
    }

    private static double Coverage(Rectangle first, Rectangle second)
    {
        var intersection = Rectangle.Intersect(first, second);
        if (intersection.Width <= 0 || intersection.Height <= 0) return 0;
        var smallerArea = Math.Min((long)first.Width * first.Height, (long)second.Width * second.Height);
        return smallerArea == 0 ? 0 : (long)intersection.Width * intersection.Height / (double)smallerArea;
    }

    private static bool SameBaseline(Rectangle first, Rectangle second)
    {
        var overlap = Math.Max(0, Math.Min(first.Bottom, second.Bottom) - Math.Max(first.Top, second.Top));
        return overlap >= Math.Min(first.Height, second.Height) * 0.6;
    }

    private static int HorizontalOverlap(Rectangle first, Rectangle second) =>
        Math.Max(0, Math.Min(first.Right, second.Right) - Math.Max(first.Left, second.Left));

    private static int HorizontalGap(Rectangle first, Rectangle second) =>
        HorizontalOverlap(first, second) > 0
            ? 0
            : Math.Max(first.Left, second.Left) - Math.Min(first.Right, second.Right);

    private static string DominantLanguage(
        IReadOnlyList<OcrWord> words,
        IReadOnlyList<string> languageOrder,
        string fallback) => words
        .GroupBy(word => word.LanguageTag, StringComparer.OrdinalIgnoreCase)
        .Select(group => new
        {
            Tag = group.First().LanguageTag,
            Letters = group.Sum(word => word.Text.Count(char.IsLetter)),
            Order = LanguageIndex(group.First().LanguageTag, languageOrder),
        })
        .OrderByDescending(item => item.Letters)
        .ThenBy(item => item.Order)
        .Select(item => item.Tag)
        .FirstOrDefault() ?? fallback;

    private static string Normalize(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static int LanguageIndex(string tag, IReadOnlyList<string> order)
    {
        for (var index = 0; index < order.Count; index++)
            if (string.Equals(tag, order[index], StringComparison.OrdinalIgnoreCase)) return index;
        return int.MaxValue;
    }

    private sealed record MergedLine(string LanguageTag, Rectangle Bounds, IReadOnlyList<OcrWord> Words);
}
