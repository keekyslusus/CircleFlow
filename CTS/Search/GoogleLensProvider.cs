namespace CircleToSearch.Search;

public enum GoogleLensSearchStatus
{
    ResultsReady,
    RuntimeUnavailable,
    Failed,
    Canceled,
}

public sealed class GoogleLensProvider : IVisualSearchProvider, IDisposable
{
    private readonly Func<byte[], CancellationToken, Task<GoogleLensSearchStatus>> _show;
    private readonly IDisposable? _ownedWindow;
    private int _disposed;

    public GoogleLensProvider(GoogleLensWindow window)
        : this(window.ShowAsync, window)
    {
    }

    public GoogleLensProvider(
        Func<byte[], CancellationToken, Task<GoogleLensSearchStatus>> show)
        : this(show, null)
    {
    }

    internal GoogleLensProvider(
        Func<byte[], CancellationToken, Task<GoogleLensSearchStatus>> show,
        IDisposable? ownedWindow)
    {
        _show = show;
        _ownedWindow = ownedWindow;
    }

    public async Task<VisualSearchOutcome> SearchAsync(byte[] png, CancellationToken cancel)
    {
        var status = await _show(png, cancel).ConfigureAwait(false);
        return status switch
        {
            GoogleLensSearchStatus.ResultsReady => VisualSearchOutcome.Handled(),
            GoogleLensSearchStatus.RuntimeUnavailable =>
                VisualSearchOutcome.Fail(UploadFailure.BrowserRuntimeUnavailable),
            GoogleLensSearchStatus.Canceled => VisualSearchOutcome.Fail(UploadFailure.Canceled),
            _ => VisualSearchOutcome.Fail(UploadFailure.BrowserAutomationFailed),
        };
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _ownedWindow?.Dispose();
    }
}
