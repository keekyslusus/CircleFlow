using CircleToSearch.Capture;

namespace CircleToSearch.Search;

internal sealed class OverlayAskSession(
    ImageAskWorkflow workflow,
    int maxLongSidePx,
    CancellationToken sessionCancellation) : IOverlaySessionOperation
{
    private CancellationTokenSource? _cancellation;
    private TaskCompletionSource<byte[]>? _image;
    private TaskCompletionSource<string>? _question;
    private Task? _pending;

    public Task? PendingTask => _pending;

    public void Start()
    {
        if (_pending is { IsCompleted: true }) Clear();
        if (_pending is not null) return;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(sessionCancellation);
        _image = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _question = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = workflow.PresentAsync(_image.Task, _question.Task, _cancellation.Token);
    }

    public void AttachImage(SelectionOutcome selection)
    {
        if (_image is null || _image.Task.IsCompleted)
        {
            selection.Dispose();
            return;
        }
        _image.TrySetResult(workflow.Encode(selection, maxLongSidePx));
    }

    public async Task SubmitAsync(AskAboutSelection asked, Action onUploadStarted)
    {
        Start();
        AttachImage(asked.Selection);
        _question!.TrySetResult(asked.Question);
        onUploadStarted();
        await _pending!.ConfigureAwait(false);
        Clear();
    }

    public async Task CancelAsync()
    {
        if (_pending is null) return;
        _cancellation!.Cancel();
        await DrainAsync().ConfigureAwait(false);
    }

    public async Task<OverlaySessionContinuation> CompletePendingAsync()
    {
        // The draft ended before a question, e.g. the browser could not start; the presenter reported it.
        await DrainAsync().ConfigureAwait(false);
        return OverlaySessionContinuation.Continue;
    }

    public void RequestStop() => _cancellation?.Cancel();

    public async Task DrainAsync()
    {
        try
        {
            if (_pending is not null) await _pending.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        finally
        {
            Clear();
        }
    }

    private void Clear()
    {
        _pending = null;
        _image = null;
        _question = null;
        _cancellation?.Dispose();
        _cancellation = null;
    }
}
