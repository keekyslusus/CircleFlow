using CircleToSearch.Capture;

namespace CircleToSearch.Search;

internal sealed class OverlayLensSession(
    LensPrewarmWorkflow workflow,
    int maxLongSidePx,
    CancellationToken sessionCancellation) : IOverlaySessionOperation
{
    private readonly PendingPresentation _presentation = new(sessionCancellation);
    private TaskCompletionSource<byte[]>? _image;
    private TaskCompletionSource? _reveal;

    public Task? PendingTask => _presentation.Pending;

    public bool IsWarming => _presentation.IsRunning;

    public static bool Handles(string providerId) =>
        string.Equals(providerId, SearchProviderIds.GoogleLens, StringComparison.OrdinalIgnoreCase);

    public void Start() => _presentation.Start(cancellation =>
    {
        _image = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _reveal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return workflow.PresentAsync(_image.Task, _reveal.Task, cancellation);
    });

    public async Task SubmitAsync(SelectionOutcome selection, Task overlayClosed, Action onUploadStarted)
    {
        // The upload runs hidden while the overlay plays its closing hold.
        _image!.TrySetResult(workflow.Encode(selection, maxLongSidePx));
        await overlayClosed.ConfigureAwait(false);
        // Until the overlay is gone the hotkey still cancels the search, as it does on the normal path.
        onUploadStarted();
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
