using CircleToSearch.Search.Browser;

namespace CircleToSearch.Search;

internal static class GoogleTrafficCheck
{
    private static readonly TimeSpan UserTimeout = TimeSpan.FromMinutes(3);

    internal static bool IsShown(Uri? uri) =>
        uri is { Scheme: "https", Host: "google.com" or "www.google.com" } &&
        uri.AbsolutePath.StartsWith("/sorry/", StringComparison.Ordinal);

    // The user solves the check in the visible browser, which then continues to the blocked page.
    internal static async Task<bool> WaitForUserAsync(IVisualSearchBrowserSession session, CancellationToken cancel)
    {
        var deadline = DateTime.UtcNow + UserTimeout;
        while (IsShown(session.CurrentUri))
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) return false;
            var navigation = await session.WaitForNavigationAsync(remaining, cancel).ConfigureAwait(true);
            if (navigation.Status == BrowserNavigationStatus.Canceled)
                throw new OperationCanceledException(cancel);
            if (navigation.Status == BrowserNavigationStatus.TimedOut) return false;
        }
        return true;
    }
}
