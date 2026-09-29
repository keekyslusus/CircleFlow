using System.Text.Json;
using CircleToSearch.Search.Browser;

namespace CircleToSearch.Search;

// The browser starts while the text is still being selected; the results address arrives once the user searches.
internal sealed class TextSearchBrowserOperation(Task<Uri> results, Uri? preconnect = null) : IVisualSearchBrowserOperation
{
    private static readonly TimeSpan NavigationTimeout = TimeSpan.FromSeconds(20);

    public async Task<VisualSearchBrowserOperationStatus> ExecuteAsync(
        IVisualSearchBrowserSession session,
        CancellationToken cancel)
    {
        Uri target;
        try
        {
            if (preconnect is not null && !results.IsCompleted)
                await PreconnectAsync(session, preconnect, cancel).ConfigureAwait(true);
            target = await results.WaitAsync(cancel).ConfigureAwait(true);
        }
        catch (OperationCanceledException) { return VisualSearchBrowserOperationStatus.Canceled; }

        var navigation = await session.NavigateAsync(target, NavigationTimeout, cancel).ConfigureAwait(true);
        return navigation.Status switch
        {
            BrowserNavigationStatus.Succeeded => VisualSearchBrowserOperationStatus.Succeeded,
            BrowserNavigationStatus.Canceled => VisualSearchBrowserOperationStatus.Canceled,
            _ => VisualSearchBrowserOperationStatus.Failed,
        };
    }

    // Opens only the DNS, TCP and TLS connection, so the search site receives no request until the user searches.
    // The hint goes into the new window's initial blank document: navigating to a page of our own would leave
    // an entry that Alt+Left returns to from the results.
    private static async Task PreconnectAsync(IVisualSearchBrowserSession session, Uri origin, CancellationToken cancel)
    {
        try
        {
            await session.ExecuteScriptAsync(
                $$"""
                (() => {
                    const hint = document.createElement('link');
                    hint.rel = 'preconnect';
                    hint.href = {{JsonSerializer.Serialize(origin.AbsoluteUri)}};
                    (document.head || document.documentElement).appendChild(hint);
                })();
                """,
                cancel).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A missing hint only costs the head start; the search itself still works.
        }
    }
}
