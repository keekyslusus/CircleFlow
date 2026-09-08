using System.Drawing;

namespace CircleToSearch.Translation;

public sealed record TranslationChunk
{
    public TranslationChunk(int lineId, int order, string text)
    {
        if (lineId < 0) throw new ArgumentOutOfRangeException(nameof(lineId));
        if (order < 0) throw new ArgumentOutOfRangeException(nameof(order));
        ArgumentException.ThrowIfNullOrEmpty(text);
        LineId = lineId;
        Order = order;
        Text = text;
    }

    public int LineId { get; }
    public int Order { get; }
    public string Text { get; }
}

public sealed record TranslatedChunk(int LineId, int Order, string SourceText, string? TranslatedText, TranslationFailure Failure);

public enum TranslationFailure
{
    None,
    NoText,
    SameLanguage,
    Network,
    Timeout,
    RateLimited,
    Service,
    BadResponse,
    Canceled,
}

public sealed record TranslationBatchOutcome(IReadOnlyList<TranslatedChunk> Chunks)
{
    public bool HasSuccess => Chunks.Any(chunk => chunk.Failure == TranslationFailure.None);
    public bool HasPartialFailure => HasSuccess && Chunks.Any(chunk => chunk.Failure != TranslationFailure.None);
}

public sealed record ScreenTranslationLine(int LineId, Rectangle SourceBoundsPx, string SourceText, string TranslatedText);

public sealed record ScreenTranslationResult(
    Guid RequestId,
    IReadOnlyList<ScreenTranslationLine> Lines,
    bool IsPartial)
{
    public System.Windows.Media.Imaging.BitmapSource? Image { get; init; }
    public string? TargetLanguageTag { get; init; }
}

public sealed record ScreenTranslationOutcome(
    ScreenTranslationResult? Result,
    TranslationFailure Failure)
{
    public bool Success => Result is not null;
    public static ScreenTranslationOutcome Succeeded(ScreenTranslationResult result) => new(result, TranslationFailure.None);
    public static ScreenTranslationOutcome Failed(TranslationFailure failure) => new(null, failure);
}
