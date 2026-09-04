using CircleToSearch.TextRecognition;

namespace CircleToSearch.Translation;

public sealed class ScreenTranslationWorkflow(
    ITranslationProvider provider,
    TranslationSegmenter segmenter)
{
    public async Task<ScreenTranslationOutcome> TranslateAsync(
        Guid requestId,
        OcrDocument document,
        string targetLanguageTag,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLanguageTag);
        if (document.Lines.Count == 0) return ScreenTranslationOutcome.Failed(TranslationFailure.NoText);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var linePlans = document.Lines.Select(line => BuildLinePlan(line, targetLanguageTag)).ToArray();
            var translatableRuns = linePlans.SelectMany(plan => plan.Runs)
                .Where(run => run.RequiresTranslation)
                .ToArray();
            if (translatableRuns.Length == 0)
                return ScreenTranslationOutcome.Failed(TranslationFailure.SameLanguage);

            AssignChunks(linePlans);
            var batches = new List<TranslationBatchOutcome>();
            foreach (var languageGroup in translatableRuns.GroupBy(
                         run => run.LanguageTag,
                         StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunks = languageGroup.SelectMany(run => run.Chunks).ToArray();
                var batch = await provider.TranslateAsync(
                    chunks,
                    languageGroup.Key,
                    targetLanguageTag,
                    cancellationToken).ConfigureAwait(false);
                if (batch.Chunks.Any(chunk => chunk.Failure == TranslationFailure.Canceled))
                    return ScreenTranslationOutcome.Failed(TranslationFailure.Canceled);
                batches.Add(batch);
            }

            var translatedChunks = batches.SelectMany(batch => batch.Chunks)
                .GroupBy(chunk => (chunk.LineId, chunk.Order))
                .ToDictionary(group => group.Key, group => group.Last());
            var translatedLines = new List<ScreenTranslationLine>();
            foreach (var plan in linePlans.Where(plan => plan.Runs.Any(run => run.RequiresTranslation)))
            {
                var translatedRuns = new List<string>(plan.Runs.Count);
                var failed = false;
                foreach (var run in plan.Runs)
                {
                    if (!run.RequiresTranslation)
                    {
                        translatedRuns.Add(run.Text);
                        continue;
                    }

                    var results = run.Chunks.Select(chunk => translatedChunks.GetValueOrDefault((chunk.LineId, chunk.Order)))
                        .ToArray();
                    if (results.Any(result => result is null || result.Failure != TranslationFailure.None ||
                                              string.IsNullOrEmpty(result.TranslatedText)))
                    {
                        failed = true;
                        break;
                    }
                    translatedRuns.Add(string.Concat(results.Select(result => result!.TranslatedText)));
                }
                if (failed) continue;
                translatedLines.Add(new ScreenTranslationLine(
                    plan.Line.Id,
                    plan.Line.BoundsPx,
                    plan.Line.Text,
                    string.Join(' ', translatedRuns)));
            }

            if (translatedLines.Count == 0)
            {
                var failure = batches.SelectMany(batch => batch.Chunks)
                    .FirstOrDefault(chunk => chunk.Failure != TranslationFailure.None)?.Failure ??
                    TranslationFailure.Service;
                return ScreenTranslationOutcome.Failed(failure);
            }
            var translatableLineCount = linePlans.Count(plan => plan.Runs.Any(run => run.RequiresTranslation));
            return ScreenTranslationOutcome.Succeeded(new ScreenTranslationResult(
                requestId,
                translatedLines,
                batches.Any(batch => batch.HasPartialFailure || !batch.HasSuccess) ||
                translatedLines.Count != translatableLineCount));
        }
        catch (OperationCanceledException)
        {
            return ScreenTranslationOutcome.Failed(TranslationFailure.Canceled);
        }
    }

    private void AssignChunks(IReadOnlyList<LinePlan> linePlans)
    {
        foreach (var plan in linePlans)
        {
            var order = 0;
            foreach (var run in plan.Runs.Where(run => run.RequiresTranslation))
            {
                run.Chunks = segmenter.Segment(plan.Line.Id, run.Text)
                    .Select(chunk => new TranslationChunk(plan.Line.Id, order++, chunk.Text))
                    .ToArray();
            }
        }
    }

    private static LinePlan BuildLinePlan(OcrLine line, string targetLanguageTag)
    {
        var words = line.Words.OrderBy(word => word.ReadingOrder).ToArray();
        var effectiveTags = new string[words.Length];
        for (var index = 0; index < words.Length; index++)
        {
            if (words[index].Text.Any(char.IsLetter))
            {
                effectiveTags[index] = words[index].LanguageTag;
                continue;
            }
            effectiveTags[index] = index > 0
                ? effectiveTags[index - 1]
                : words.Skip(index + 1).FirstOrDefault(word => word.Text.Any(char.IsLetter))?.LanguageTag ?? line.LanguageTag;
        }

        var runs = new List<TranslationRun>();
        for (var index = 0; index < words.Length; index++)
        {
            var current = runs.LastOrDefault();
            if (current is null || !TranslationLanguageTags.Equivalent(current.LanguageTag, effectiveTags[index]))
            {
                current = new TranslationRun(effectiveTags[index]);
                runs.Add(current);
            }
            current.Words.Add(words[index].Text);
        }
        foreach (var run in runs)
        {
            run.Text = string.Join(' ', run.Words);
            run.RequiresTranslation = run.Text.Any(char.IsLetter) &&
                                      !TranslationLanguageTags.Equivalent(run.LanguageTag, targetLanguageTag);
        }
        return new LinePlan(line, runs);
    }

    private sealed record LinePlan(OcrLine Line, IReadOnlyList<TranslationRun> Runs);

    private sealed class TranslationRun(string languageTag)
    {
        internal string LanguageTag { get; } = languageTag;
        internal List<string> Words { get; } = [];
        internal string Text { get; set; } = string.Empty;
        internal bool RequiresTranslation { get; set; }
        internal IReadOnlyList<TranslationChunk> Chunks { get; set; } = [];
    }
}
