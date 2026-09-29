using System.Net;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace CircleToSearch.Translation;

public sealed class ScreenTranslationWorkflow
{
    private readonly IImageTranslationProvider _images;
    private readonly PluginLog _log;

    internal ScreenTranslationWorkflow(IImageTranslationProvider images, PluginLog log)
    {
        _images = images;
        _log = log;
    }

    public async Task<ScreenTranslationOutcome> TranslateAsync(Guid requestId, BitmapSource image,
        string targetLanguageTag, CancellationToken cancellationToken)
    {
        if (requestId == Guid.Empty) throw new ArgumentException("A translation request id is required.", nameof(requestId));
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLanguageTag);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            var target = NormalizeTarget(targetLanguageTag);
            var translatedImage = await _images.TranslateAsync(image, target, timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            return ScreenTranslationOutcome.Succeeded(new ScreenTranslationResult(requestId, translatedImage));
        }
        catch (OperationCanceledException)
        {
            return ScreenTranslationOutcome.Failed(cancellationToken.IsCancellationRequested ? TranslationFailure.Canceled : TranslationFailure.Timeout);
        }
        catch (TimeoutException) { return ScreenTranslationOutcome.Failed(TranslationFailure.Timeout); }
        catch (HttpRequestException error)
        {
            return ScreenTranslationOutcome.Failed(error.StatusCode == HttpStatusCode.TooManyRequests
                ? TranslationFailure.RateLimited : error.StatusCode is null ? TranslationFailure.Network : TranslationFailure.Service);
        }
        catch (Exception exception)
        {
            _log.SafeError(nameof(ScreenTranslationWorkflow), "translate", exception);
            return ScreenTranslationOutcome.Failed(TranslationFailure.BadResponse);
        }
    }

    internal static string NormalizeTarget(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        var tag = target.Trim().Replace('_', '-');
        if (tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return tag.Contains("TW", StringComparison.OrdinalIgnoreCase) || tag.Contains("Hant", StringComparison.OrdinalIgnoreCase)
                || tag.Contains("HK", StringComparison.OrdinalIgnoreCase) ? "zh-TW" : "zh-CN";
        return tag.Split('-')[0].ToLowerInvariant();
    }
}
