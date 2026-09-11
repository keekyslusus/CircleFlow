using System.Windows.Media.Imaging;

namespace CircleToSearch.Translation;

public enum TranslationFailure
{
    None,
    Network,
    Timeout,
    RateLimited,
    Service,
    BadResponse,
    Canceled,
}

public sealed record ScreenTranslationResult
{
    public ScreenTranslationResult(Guid requestId, BitmapSource image)
    {
        if (requestId == Guid.Empty) throw new ArgumentException("A translation request id is required.", nameof(requestId));
        RequestId = requestId;
        Image = image ?? throw new ArgumentNullException(nameof(image));
    }

    public Guid RequestId { get; }
    public BitmapSource Image { get; }
}

public sealed record ScreenTranslationOutcome(
    ScreenTranslationResult? Result,
    TranslationFailure Failure)
{
    public static ScreenTranslationOutcome Succeeded(ScreenTranslationResult result) => new(result, TranslationFailure.None);
    public static ScreenTranslationOutcome Failed(TranslationFailure failure) => new(null, failure);
}
