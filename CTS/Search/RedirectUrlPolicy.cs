namespace CircleToSearch.Search;

public static class RedirectUrlPolicy
{
    public static bool IsAllowed(Uri? url)
        => url is { IsAbsoluteUri: true }
           && url.Scheme == Uri.UriSchemeHttps
           && (url.Host == "google.com" || url.Host.EndsWith(".google.com", StringComparison.Ordinal));
}
