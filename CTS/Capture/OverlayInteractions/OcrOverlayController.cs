using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.TextRecognition;
using CircleToSearch.Translation;

namespace CircleToSearch.Capture.OverlayInteractions;

internal sealed class OcrOverlayController(
    BitmapSource source,
    Dispatcher uiDispatcher,
    IOcrRecognizer recognizer,
    string? requestedLanguageTag,
    Action<OcrRecognitionOutcome> completed,
    PluginLog? log = null,
    TranslationMemoryProfiler? profiler = null) : IDisposable
{
    private CancellationTokenSource _cancellation = new();
    private int _generation;
    private bool _started;
    private bool _disposed;
    private CachedRecognition? _originalCache;
    private CachedRecognition? _translatedCache;

    private sealed record CachedRecognition(BitmapSource Image, string? Language, OcrRecognitionOutcome Outcome);

    internal void Start()
    {
        if (_started || _disposed) return;
        _started = true;
        var generation = ++_generation;
        _ = RunAsync(generation, source, requestedLanguageTag, _cancellation.Token);
    }

    internal void Restart(BitmapSource image, string? language)
    {
        if (_disposed) return;
        Invalidate();
        _started = true;
        _ = RunAsync(_generation, image, language, _cancellation.Token);
    }

    internal void Invalidate()
    {
        if (_disposed) return;
        _cancellation.Cancel();
        _cancellation.Dispose();
        _cancellation = new CancellationTokenSource();
        ++_generation;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _generation++;
        _originalCache = null;
        _translatedCache = null;
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    private async Task RunAsync(int generation, BitmapSource image, string? language, CancellationToken cancellation)
    {
        OcrRecognitionOutcome outcome;
        var entry = ReferenceEquals(image, source) ? _originalCache : _translatedCache;
        var cached = entry is not null && ReferenceEquals(entry.Image, image) &&
            string.Equals(entry.Language, language, StringComparison.OrdinalIgnoreCase) ? entry.Outcome : null;
        try
        {
            outcome = cached ?? await recognizer.RecognizeAsync(image, language, cancellation)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            outcome = OcrRecognitionOutcome.Canceled();
        }
        catch (Exception exception)
        {
            log?.Error(nameof(OcrOverlayController), "OCR recognition failed unexpectedly.", exception);
            outcome = OcrRecognitionOutcome.Failed();
        }

        log?.Info(nameof(OcrOverlayController), cached is null
            ? $"OCR completed with status {outcome.Status}." : $"OCR restored from cache with status {outcome.Status}.");
        if (_disposed || generation != _generation || uiDispatcher.HasShutdownStarted || uiDispatcher.HasShutdownFinished)
            return;

        try
        {
            await uiDispatcher.InvokeAsync(() =>
            {
                if (_disposed || generation != _generation) return;
                if (cached is null && image.IsFrozen && outcome.Status is OcrRecognitionStatus.Success or OcrRecognitionStatus.NoText)
                {
                    var result = new CachedRecognition(image, language, outcome);
                    if (ReferenceEquals(image, source)) _originalCache = result;
                    else _translatedCache = result;
                }
                completed(outcome);
                profiler?.Mark(cached is null ? "ocr_complete" : "ocr_cache_hit");
            });
        }
        catch (TaskCanceledException) { }
        catch (InvalidOperationException) when (uiDispatcher.HasShutdownStarted || uiDispatcher.HasShutdownFinished) { }
        catch (Exception exception)
        {
            log?.Error(nameof(OcrOverlayController), "OCR result delivery failed unexpectedly.", exception);
        }
    }
}
