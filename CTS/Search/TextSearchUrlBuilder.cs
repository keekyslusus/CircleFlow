using System.Text;

namespace CircleToSearch.Search;

public sealed class TextSearchUrlBuilder(int maximumScalarValues = 2000)
{
    public int MaximumScalarValues { get; } = maximumScalarValues > 0
        ? maximumScalarValues
        : throw new ArgumentOutOfRangeException(nameof(maximumScalarValues));

    public string Build(string text, string providerId, string engineId = TextSearchEngines.MatchImageSearch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        if (text.EnumerateRunes().Take(MaximumScalarValues + 1).Count() > MaximumScalarValues)
            throw new TextSearchQueryTooLongException();
        return Template(providerId, engineId)
            .Replace(TextSearchEngines.QueryPlaceholder, Uri.EscapeDataString(text), StringComparison.Ordinal);
    }

    public string SiteName(string providerId, string engineId = TextSearchEngines.MatchImageSearch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        var host = new Uri(Template(providerId, engineId)
            .Replace(TextSearchEngines.QueryPlaceholder, string.Empty, StringComparison.Ordinal)).Host;
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    private static string Template(string providerId, string engineId)
    {
        if (engineId != TextSearchEngines.MatchImageSearch)
        {
            return (TextSearchEngines.Find(engineId)
                ?? throw new ArgumentException("Unsupported search engine.", nameof(engineId))).UrlTemplate;
        }
        return providerId.ToLowerInvariant() switch
        {
            SearchProviderIds.TraceMoe => "https://anilist.co/search/anime?search=%s",
            SearchProviderIds.GoogleLens => "https://www.google.com/search?q=%s",
            SearchProviderIds.YandexImages => "https://yandex.com/search/?text=%s",
            _ => throw new ArgumentException("Unsupported search provider.", nameof(providerId)),
        };
    }
}

public sealed class TextSearchQueryTooLongException : ArgumentException
{
    public TextSearchQueryTooLongException() : base("The search text exceeds the supported URL limit.") { }
}
