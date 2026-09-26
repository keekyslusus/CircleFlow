using CircleToSearch.Search.Browser;

namespace CircleToSearch.Search;

// The browser starts while the text is still being selected; the results address arrives once the user searches.
internal sealed class TextSearchBrowserOperation(Task<Uri> results) : IVisualSearchBrowserOperation
{
    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(20);

    public async Task<VisualSearchBrowserOperationStatus> ExecuteAsync(
        IVisualSearchBrowserSession session,
        CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(session);
        Uri target;
        try { target = await results.WaitAsync(cancel).ConfigureAwait(true); }
        catch (OperationCanceledException) { return VisualSearchBrowserOperationStatus.Canceled; }

        var navigation = await session.NavigateAsync(target, NavigationTimeout, cancel).ConfigureAwait(true);
        return navigation.Status switch
        {
            BrowserNavigationStatus.Succeeded => VisualSearchBrowserOperationStatus.Succeeded,
            BrowserNavigationStatus.Canceled => VisualSearchBrowserOperationStatus.Canceled,
            _ => VisualSearchBrowserOperationStatus.Failed,
        };
    }
}
