namespace CircleToSearch.Search;

// One search an overlay session starts before the user commits to it, so it can still be canceled.
internal sealed class PendingPresentation(CancellationToken sessionCancellation)
{
    private CancellationTokenSource? _cancellation;

    public Task? Pending { get; private set; }

    public bool IsRunning => Pending is { IsCompleted: false };

    public void Start(Func<CancellationToken, Task> present)
    {
        if (Pending is { IsCompleted: true }) Clear();
        if (Pending is not null) return;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(sessionCancellation);
        Pending = present(_cancellation.Token);
    }

    public async Task CompleteAsync()
    {
        if (Pending is not null) await Pending.ConfigureAwait(false);
        Clear();
    }

    public async Task CancelAsync()
    {
        if (Pending is null) return;
        _cancellation!.Cancel();
        await DrainAsync().ConfigureAwait(false);
    }

    public void RequestStop() => _cancellation?.Cancel();

    public async Task DrainAsync()
    {
        try
        {
            if (Pending is not null) await Pending.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        finally
        {
            Clear();
        }
    }

    private void Clear()
    {
        Pending = null;
        _cancellation?.Dispose();
        _cancellation = null;
    }
}
