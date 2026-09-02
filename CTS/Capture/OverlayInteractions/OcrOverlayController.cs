using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CircleToSearch.TextRecognition;

namespace CircleToSearch.Capture.OverlayInteractions;

internal sealed class OcrOverlayController(
    BitmapSource source,
    Dispatcher uiDispatcher,
    IOcrRecognizer recognizer,
    string? requestedLanguageTag,
    Action<OcrRecognitionOutcome> completed,
    PluginLog? log = null) : IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private int _generation;
    private bool _started;
    private bool _disposed;

    internal void Start()
    {
        if (_started || _disposed) return;
        _started = true;
        var generation = ++_generation;
        _ = RunAsync(generation);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _generation++;
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    private async Task RunAsync(int generation)
    {
        OcrRecognitionOutcome outcome;
        try
        {
            outcome = await recognizer.RecognizeAsync(source, requestedLanguageTag, _cancellation.Token)
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

        log?.Info(nameof(OcrOverlayController), $"OCR completed with status {outcome.Status}.");
        if (_disposed || generation != _generation || uiDispatcher.HasShutdownStarted || uiDispatcher.HasShutdownFinished)
            return;

        try
        {
            await uiDispatcher.InvokeAsync(() =>
            {
                if (!_disposed && generation == _generation) completed(outcome);
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
