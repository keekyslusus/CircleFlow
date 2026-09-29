using System.Windows.Media.Imaging;

namespace CircleToSearch.TextRecognition;

public interface IOcrRecognizer
{
    Task<OcrRecognitionOutcome> RecognizeAsync(
        BitmapSource source,
        string? requestedLanguageTag,
        CancellationToken cancellationToken);
}

public enum OcrRecognitionStatus
{
    Success,
    NoText,
    LanguageUnavailable,
    PlatformUnavailable,
    Canceled,
    Failed,
}

public sealed record OcrRecognitionOutcome
{
    private OcrRecognitionOutcome(OcrRecognitionStatus status, OcrDocument? document) =>
        (Status, Document) = (status, document);

    public OcrRecognitionStatus Status { get; }
    public OcrDocument? Document { get; }

    public static OcrRecognitionOutcome Success(OcrDocument document) =>
        new(OcrRecognitionStatus.Success, document);
    public static OcrRecognitionOutcome NoText() => new(OcrRecognitionStatus.NoText, null);
    public static OcrRecognitionOutcome LanguageUnavailable() => new(OcrRecognitionStatus.LanguageUnavailable, null);
    public static OcrRecognitionOutcome PlatformUnavailable() => new(OcrRecognitionStatus.PlatformUnavailable, null);
    public static OcrRecognitionOutcome Canceled() => new(OcrRecognitionStatus.Canceled, null);
    public static OcrRecognitionOutcome Failed() => new(OcrRecognitionStatus.Failed, null);
}

internal sealed class DisabledOcrRecognizer : IOcrRecognizer
{
    internal static DisabledOcrRecognizer Instance { get; } = new();

    public Task<OcrRecognitionOutcome> RecognizeAsync(
        BitmapSource source,
        string? requestedLanguageTag,
        CancellationToken cancellationToken) =>
        Task.FromResult(OcrRecognitionOutcome.PlatformUnavailable());
}
