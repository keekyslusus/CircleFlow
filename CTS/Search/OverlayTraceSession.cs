using CircleToSearch.Capture;

namespace CircleToSearch.Search;

internal sealed class OverlayTraceSession(
    IOverlaySession overlay,
    VisualSearchWorkflow workflow,
    int maxLongSidePx,
    Func<string, bool>? openTraceUrl,
    CancellationToken sessionCancellation) : IOverlaySessionOperation
{
    private CancellationTokenSource? _cancellation =
        CancellationTokenSource.CreateLinkedTokenSource(sessionCancellation);
    private Task<VisualSearchPreparationOutcome>? _pending;
    private TraceMoeMatch? _match;

    public Task? PendingTask => _pending;

    public bool CanStart(VisualSelection selection) =>
        selection.ProviderId == SearchProviderIds.TraceMoe && _pending is null;

    public void Start(VisualSelection selection)
    {
        if (!CanStart(selection)) return;
        _pending = workflow.PrepareTraceAsync(
            selection.Selection,
            maxLongSidePx,
            _cancellation!.Token);
    }

    public async Task<OverlaySessionContinuation> OpenResultAsync()
    {
        if (_match is null) return OverlaySessionContinuation.Continue;
        await overlay.CloseAsync().ConfigureAwait(false);
        openTraceUrl?.Invoke(_match.AnilistUrl);
        return OverlaySessionContinuation.EndSession;
    }

    public async Task<OverlaySessionContinuation> CompletePendingAsync()
    {
        var pending = _pending ?? throw new InvalidOperationException("No trace search is pending.");
        var outcome = await pending.ConfigureAwait(false);
        _pending = null;
        _match = outcome.PreparedSearch?.TraceMatch;
        if (!sessionCancellation.IsCancellationRequested && outcome.Failure != UploadFailure.Canceled)
            await overlay.ShowTraceResultAsync(outcome, sessionCancellation).ConfigureAwait(false);
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
            _pending = null;
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }
}
