using CircleToSearch.Capture;
using CircleToSearch.Search;

namespace CircleToSearch.Translation;

internal sealed class OverlayTranslationSession(
    IOverlaySession overlay,
    ScreenTranslationWorkflow workflow,
    CancellationToken sessionCancellation) : IOverlaySessionOperation
{
    private CancellationTokenSource? _cancellation;
    private Task<ScreenTranslationOutcome>? _pending;
    private Guid _requestId;

    public Task? PendingTask => _pending;
    public bool IsRunning => _pending is not null;

    public void Start(ScreenTranslationRequested request)
    {
        if (_pending is not null) return;
        _requestId = request.RequestId;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(sessionCancellation);
        _pending = workflow.TranslateAsync(
            request.RequestId,
            request.Image,
            request.TargetLanguageTag,
            _cancellation.Token);
    }

    public async Task CancelAsync(CancelScreenTranslation command)
    {
        if (_pending is null || command.RequestId != _requestId) return;
        _cancellation?.Cancel();
        try { await _pending.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        ClearPending();
    }

    public async Task<OverlaySessionContinuation> CompletePendingAsync()
    {
        var pending = _pending ?? throw new InvalidOperationException("No translation is pending.");
        var requestId = _requestId;
        var outcome = await pending.ConfigureAwait(false);
        ClearPending();
        if (sessionCancellation.IsCancellationRequested || outcome.Failure == TranslationFailure.Canceled)
            return OverlaySessionContinuation.Continue;
        if (outcome.Result is { } result)
            await overlay.ShowTranslationAsync(result, sessionCancellation).ConfigureAwait(false);
        else
            await overlay.ShowTranslationFailureAsync(requestId, outcome.Failure, sessionCancellation)
                .ConfigureAwait(false);
        return OverlaySessionContinuation.Continue;
    }

    public void RequestStop() => _cancellation?.Cancel();

    public async Task DrainAsync()
    {
        try
        {
            if (_pending is not null) await _pending.ConfigureAwait(false);
        }
        finally
        {
            ClearPending();
        }
    }

    private void ClearPending()
    {
        _pending = null;
        _requestId = Guid.Empty;
        _cancellation?.Dispose();
        _cancellation = null;
    }
}
