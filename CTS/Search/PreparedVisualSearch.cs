using CircleToSearch.Search.Browser;

namespace CircleToSearch.Search;

public enum PreparedVisualSearchKind
{
    Url,
    BrowserOperation,
    TraceMoe,
}

public sealed class PreparedVisualSearch
{
    private PreparedVisualSearch(
        PreparedVisualSearchKind kind,
        Uri? resultsUrl,
        IVisualSearchBrowserOperation? browserOperation,
        Uri? externalFallbackUrl)
    {
        Kind = kind;
        ResultsUrl = resultsUrl;
        BrowserOperation = browserOperation;
        ExternalFallbackUrl = externalFallbackUrl;
    }

    public PreparedVisualSearchKind Kind { get; }
    public TraceMoeMatch? TraceMatch { get; private init; }

    public static PreparedVisualSearch ForTraceMoe(TraceMoeMatch? match) =>
        new(PreparedVisualSearchKind.TraceMoe, null, null, null) { TraceMatch = match };

    public Uri? ExternalFallbackUrl { get; }

    internal Uri? ResultsUrl { get; }

    internal IVisualSearchBrowserOperation? BrowserOperation { get; }

    public static PreparedVisualSearch ForUrl(Uri resultsUrl, Uri? externalFallbackUrl)
    {
        RequireAbsolute(resultsUrl, nameof(resultsUrl));
        RequireAbsoluteIfPresent(externalFallbackUrl, nameof(externalFallbackUrl));
        return new PreparedVisualSearch(
            PreparedVisualSearchKind.Url,
            resultsUrl,
            null,
            externalFallbackUrl);
    }

    public static PreparedVisualSearch ForBrowserOperation(
        IVisualSearchBrowserOperation operation,
        Uri? externalFallbackUrl)
    {
        ArgumentNullException.ThrowIfNull(operation);
        RequireAbsoluteIfPresent(externalFallbackUrl, nameof(externalFallbackUrl));
        return new PreparedVisualSearch(
            PreparedVisualSearchKind.BrowserOperation,
            null,
            operation,
            externalFallbackUrl);
    }

    internal Uri RequireResultsUrl()
        => Kind == PreparedVisualSearchKind.Url && ResultsUrl is not null
            ? ResultsUrl
            : throw new InvalidOperationException("The prepared search does not contain a results URL.");

    internal IVisualSearchBrowserOperation RequireBrowserOperation()
        => Kind == PreparedVisualSearchKind.BrowserOperation && BrowserOperation is not null
            ? BrowserOperation
            : throw new InvalidOperationException("The prepared search does not contain a browser operation.");

    private static void RequireAbsolute(Uri uri, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(uri, parameterName);
        if (!uri.IsAbsoluteUri) throw new ArgumentException("The URL must be absolute.", parameterName);
    }

    private static void RequireAbsoluteIfPresent(Uri? uri, string parameterName)
    {
        if (uri is { IsAbsoluteUri: false })
            throw new ArgumentException("The URL must be absolute.", parameterName);
    }
}
