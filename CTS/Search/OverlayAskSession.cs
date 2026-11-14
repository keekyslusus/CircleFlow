using CircleToSearch.Capture;

namespace CircleToSearch.Search;

internal sealed class OverlayAskSession(
    ImageAskWorkflow workflow,
    int maxLongSidePx,
    CancellationToken sessionCancellation) : IOverlaySessionOperation
{
    private readonly PendingPresentation _presentation = new(sessionCancellation);
    private TaskCompletionSource<byte[]?>? _image;
    private TaskCompletionSource<string>? _question;

    public Task? PendingTask => _presentation.Pending;

    public void Start() => _presentation.Start(cancellation =>
    {
        _image = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _question = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        return workflow.PresentAsync(_image.Task, _question.Task, cancellation);
    });

    public void AttachImage(SelectionOutcome selection)
    {
        if (!_presentation.IsRunning || _image!.Task.IsCompleted)
        {
            selection.Dispose();
            return;
        }
        _image.TrySetResult(workflow.Encode(selection, maxLongSidePx));
    }

    public async Task SubmitAsync(AskAboutSelection asked, Action onUploadStarted)
    {
        Start();
        if (!asked.IncludesImage) _image!.TrySetResult(null);
        else if (asked.Selection is { } selection) AttachImage(selection);
        else if (!_image!.Task.IsCompleted)
            _image.TrySetException(new InvalidOperationException("The question arrived without an attached image."));
        _question!.TrySetResult(asked.Question);
        onUploadStarted();
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
