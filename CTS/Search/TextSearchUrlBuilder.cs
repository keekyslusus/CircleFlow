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
        var escaped = Uri.EscapeDataString(text);
        if (engineId != TextSearchEngines.MatchImageSearch)
        {
            var engine = TextSearchEngines.Find(engineId)
                ?? throw new ArgumentException("Unsupported search engine.", nameof(engineId));
            return engine.UrlTemplate.Replace(TextSearchEngines.QueryPlaceholder, escaped, StringComparison.Ordinal);
        }
        return providerId.ToLowerInvariant() switch
        {
            SearchProviderIds.TraceMoe => $"https://anilist.co/search/anime?search={escaped}",
            SearchProviderIds.GoogleLens => $"https://www.google.com/search?q={escaped}",
            SearchProviderIds.YandexImages => $"https://yandex.com/search/?text={escaped}",
            _ => throw new ArgumentException("Unsupported search provider.", nameof(providerId)),
        };
    }
}

public sealed class TextSearchQueryTooLongException : ArgumentException
{
    public TextSearchQueryTooLongException() : base("The search text exceeds the supported URL limit.") { }
}
