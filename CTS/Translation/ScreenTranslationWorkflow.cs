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
        if (TranslationLanguageTags.Equivalent(document.LanguageTag, targetLanguageTag))
            return ScreenTranslationOutcome.Failed(TranslationFailure.SameLanguage);

        var chunks = document.Lines
            .SelectMany(line => segmenter.Segment(line.Id, line.Text))
            .ToArray();
        var batch = await provider.TranslateAsync(
            chunks,
            document.LanguageTag,
            targetLanguageTag,
            cancellationToken).ConfigureAwait(false);
        if (!batch.HasSuccess)
            return ScreenTranslationOutcome.Failed(batch.Chunks.FirstOrDefault()?.Failure ?? TranslationFailure.Service);

        var translatedLines = new List<ScreenTranslationLine>();
        foreach (var line in document.Lines)
        {
            var lineChunks = batch.Chunks.Where(chunk => chunk.LineId == line.Id).OrderBy(chunk => chunk.Order).ToArray();
            if (lineChunks.Length == 0 || lineChunks.Any(chunk => chunk.Failure != TranslationFailure.None)) continue;
            translatedLines.Add(new ScreenTranslationLine(
                line.Id,
                line.BoundsPx,
                line.Text,
                string.Concat(lineChunks.Select(chunk => chunk.TranslatedText))));
        }
        if (translatedLines.Count == 0) return ScreenTranslationOutcome.Failed(TranslationFailure.Service);
        return ScreenTranslationOutcome.Succeeded(new ScreenTranslationResult(
            requestId,
            translatedLines,
            batch.HasPartialFailure || translatedLines.Count != document.Lines.Count));
    }
}
