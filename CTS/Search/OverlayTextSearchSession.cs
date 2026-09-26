using CircleToSearch.Capture;

namespace CircleToSearch.Search;

internal sealed class OverlayTextSearchSession(
    TextSearchWorkflow workflow,
    CancellationToken sessionCancellation) : IOverlaySessionOperation
{
    private readonly PendingPresentation _presentation = new(sessionCancellation);
    private TaskCompletionSource<Uri>? _results;
    private TaskCompletionSource? _reveal;

    public Task? PendingTask => _presentation.Pending;

    public bool IsWarming => _presentation.IsRunning;

    public void Start(string providerId) => _presentation.Start(cancellation =>
    {
        _results = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);
        _reveal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return workflow.WarmAsync(providerId, _results.Task, _reveal.Task, cancellation) ?? Task.CompletedTask;
    });

    public async Task SubmitAsync(SearchSelectedText selected, Func<Task> closeOverlay, Action onBrowserStarted)
    {
        if (workflow.TryCreateResultsUrl(selected.Text, selected.ProviderId) is not { } results)
        {
            await _presentation.CancelAsync().ConfigureAwait(false);
            await closeOverlay().ConfigureAwait(false);
            await workflow.ExecuteAsync(selected.Text, selected.ProviderId, onBrowserStarted, sessionCancellation)
                .ConfigureAwait(false);
            return;
        }
        // The results load hidden while the overlay closes.
        _results!.TrySetResult(results);
        await closeOverlay().ConfigureAwait(false);
        // Until the overlay is gone the hotkey still cancels the search, as it does on the normal path.
        onBrowserStarted();
        _reveal!.TrySetResult();
        await _presentation.CompleteAsync().ConfigureAwait(false);
    }

    public Task CancelAsync() => _presentation.CancelAsync();

    public async Task<OverlaySessionContinuation> CompletePendingAsync()
    {
        await _presentation.DrainAsync().ConfigureAwait(false);
        return OverlaySessionContinuation.Continue;
    }

    public void RequestStop() => _presentation.RequestStop();

    public Task DrainAsync() => _presentation.DrainAsync();
}
