using CircleToSearch.TextRecognition;
using System.Net;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace CircleToSearch.Translation;

public sealed class ScreenTranslationWorkflow(
    ITranslationProvider? provider,
    TranslationSegmenter? segmenter)
{
    private readonly IImageTranslationProvider? _images;

    internal ScreenTranslationWorkflow(IImageTranslationProvider images) : this(null, null) => _images = images;

    public async Task<ScreenTranslationOutcome> TranslateAsync(Guid requestId, BitmapSource image,
        string targetLanguageTag, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            var target = NormalizeTarget(targetLanguageTag);
            var data = await _images!.TranslateAsync(image, target, timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            return ScreenTranslationOutcome.Succeeded(new ScreenTranslationResult(requestId, [], false)
            { Image = data.Image, TargetLanguageTag = targetLanguageTag });
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
        catch { return ScreenTranslationOutcome.Failed(TranslationFailure.BadResponse); }
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
            .SelectMany(line => segmenter!.Segment(line.Id, line.Text))
            .ToArray();
        var batch = await provider!.TranslateAsync(
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
