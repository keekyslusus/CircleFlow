namespace CircleToSearch.Search;

public enum WebView2SearchStatus
{
    ResultsReady,
    RuntimeUnavailable,
    Failed,
    Canceled,
}

public sealed class WebView2VisualSearchProvider : IVisualSearchProvider
{
    private readonly Func<byte[], CancellationToken, Task<WebView2SearchStatus>> _show;

    public WebView2VisualSearchProvider(WebView2SearchWindow window)
        : this(window.ShowAsync)
    {
    }

    public WebView2VisualSearchProvider(
        Func<byte[], CancellationToken, Task<WebView2SearchStatus>> show)
    {
        _show = show;
    }

    public async Task<VisualSearchOutcome> SearchAsync(byte[] png, CancellationToken cancel)
    {
        var status = await _show(png, cancel).ConfigureAwait(false);
        return status switch
        {
            WebView2SearchStatus.ResultsReady => VisualSearchOutcome.Handled(),
            WebView2SearchStatus.RuntimeUnavailable =>
                VisualSearchOutcome.Fail(UploadFailure.BrowserRuntimeUnavailable),
            WebView2SearchStatus.Canceled => VisualSearchOutcome.Fail(UploadFailure.Canceled),
            _ => VisualSearchOutcome.Fail(UploadFailure.BrowserAutomationFailed),
        };
    }
}
