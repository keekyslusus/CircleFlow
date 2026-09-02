namespace CircleToSearch.Translation;

public interface ITranslationProvider
{
    Task<TranslationBatchOutcome> TranslateAsync(
        IReadOnlyList<TranslationChunk> chunks,
        string sourceLanguageTag,
        string targetLanguageTag,
        CancellationToken cancellationToken);
}
