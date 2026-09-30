using CircleToSearch.Capture;
using CircleToSearch.Shell;

namespace CircleToSearch.Search;

internal sealed class OverlayWidgetSession(
    IOverlaySession overlay,
    VisualSearchWorkflow workflow,
    IReadOnlySet<string> providerIds,
    int maxLongSidePx,
    UrlOpeningService urlOpening,
    CancellationToken sessionCancellation) : IOverlaySessionOperation
{
    private CancellationTokenSource? _cancellation =
        CancellationTokenSource.CreateLinkedTokenSource(sessionCancellation);
    private Task<VisualSearchPreparationOutcome>? _pending;
    private PreparedVisualSearch? _result;

    public Task? PendingTask => _pending;

    public bool CanStart(VisualSelection selection) =>
        providerIds.Contains(selection.ProviderId) && _pending is null;

    public void Start(VisualSelection selection)
    {
        if (!CanStart(selection)) return;
        _pending = workflow.PrepareWidgetAsync(
            selection.Selection,
            selection.ProviderId,
            maxLongSidePx,
            _cancellation!.Token);
    }

    // A stale or forged command must not launch a link the shown result did not offer.
    public async Task<OverlaySessionContinuation> OpenResultAsync(Uri url)
    {
        if (_result?.OffersResultUrl(url) != true) return OverlaySessionContinuation.Continue;
        await overlay.CloseAsync().ConfigureAwait(false);
        urlOpening.TryOpen(url.AbsoluteUri);
        return OverlaySessionContinuation.EndSession;
    }

    public async Task<OverlaySessionContinuation> CompletePendingAsync()
    {
        var pending = _pending ?? throw new InvalidOperationException("No widget search is pending.");
        var outcome = await pending.ConfigureAwait(false);
        _pending = null;
        _result = outcome.PreparedSearch;
        if (!sessionCancellation.IsCancellationRequested && outcome.Failure != UploadFailure.Canceled)
            await overlay.ShowWidgetResultAsync(outcome, sessionCancellation).ConfigureAwait(false);
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
