using CircleToSearch.Search.Browser;

namespace CircleToSearch.Search;

public enum PreparedVisualSearchKind
{
    Url,
    BrowserOperation,
    TraceMoe,
    Pinterest,
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

    public IReadOnlyList<PinterestPin> PinterestPins { get; private init; } = [];

    public static PreparedVisualSearch ForPinterest(IReadOnlyList<PinterestPin> pins) =>
        new(PreparedVisualSearchKind.Pinterest, null, null, null) { PinterestPins = pins };

    internal bool OffersResultUrl(Uri url) =>
        TraceMatch?.AnilistUrl == url.AbsoluteUri || PinterestPins.Any(pin => pin.PinUrl == url.AbsoluteUri);

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

    // Lets the browser load hidden and appear only once this completes.
    internal Task? RevealAfter { get; private init; }

    public static PreparedVisualSearch ForBrowserOperation(
        IVisualSearchBrowserOperation operation,
        Uri? externalFallbackUrl,
        Task? revealAfter = null)
    {
        RequireAbsoluteIfPresent(externalFallbackUrl, nameof(externalFallbackUrl));
        return new PreparedVisualSearch(
            PreparedVisualSearchKind.BrowserOperation,
            null,
            operation,
            externalFallbackUrl) { RevealAfter = revealAfter };
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
        if (!uri.IsAbsoluteUri) throw new ArgumentException("The URL must be absolute.", parameterName);
    }

    private static void RequireAbsoluteIfPresent(Uri? uri, string parameterName)
    {
        if (uri is { IsAbsoluteUri: false })
            throw new ArgumentException("The URL must be absolute.", parameterName);
    }
}
