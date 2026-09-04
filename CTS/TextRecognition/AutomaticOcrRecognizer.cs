using System.Windows.Media.Imaging;

namespace CircleToSearch.TextRecognition;

public sealed class AutomaticOcrRecognizer(
    ILanguageOcrRecognizer languageRecognizer,
    OcrLanguageCatalog languageCatalog,
    AutomaticOcrLanguageResolver languageResolver,
    OcrDocumentMerger merger) : IOcrRecognizer
{
    public async Task<OcrRecognitionOutcome> RecognizeAsync(BitmapSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var languages = languageResolver.Resolve(languageCatalog.AvailableLanguages);
        if (languages.Count == 0) return OcrRecognitionOutcome.LanguageUnavailable();
        try
        {
            var outcomes = await Task.WhenAll(languages.Select(language =>
                languageRecognizer.RecognizeAsync(source, language, cancellationToken))).ConfigureAwait(false);
            var documents = outcomes.Where(outcome => outcome.Status == OcrRecognitionStatus.Success)
                .Select(outcome => outcome.Document!).ToArray();
            if (documents.Length > 0)
            {
                var merged = merger.Merge(documents, languages);
                return merged.Lines.Count == 0 ? OcrRecognitionOutcome.NoText() : OcrRecognitionOutcome.Success(merged);
            }
            if (outcomes.All(outcome => outcome.Status == OcrRecognitionStatus.Canceled)) return OcrRecognitionOutcome.Canceled();
            if (outcomes.Any(outcome => outcome.Status == OcrRecognitionStatus.Failed)) return OcrRecognitionOutcome.Failed();
            if (outcomes.Any(outcome => outcome.Status == OcrRecognitionStatus.PlatformUnavailable)) return OcrRecognitionOutcome.PlatformUnavailable();
            return outcomes.All(outcome => outcome.Status == OcrRecognitionStatus.LanguageUnavailable)
                ? OcrRecognitionOutcome.LanguageUnavailable()
                : OcrRecognitionOutcome.NoText();
        }
        catch (OperationCanceledException)
        {
            return OcrRecognitionOutcome.Canceled();
        }
    }
}
