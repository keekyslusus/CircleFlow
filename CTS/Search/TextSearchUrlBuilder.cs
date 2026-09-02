using System.Text;

namespace CircleToSearch.Search;

public sealed class TextSearchUrlBuilder(int maximumScalarValues = 2000)
{
    public int MaximumScalarValues { get; } = maximumScalarValues > 0
        ? maximumScalarValues
        : throw new ArgumentOutOfRangeException(nameof(maximumScalarValues));

    public string Build(string text, string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        if (text.EnumerateRunes().Take(MaximumScalarValues + 1).Count() > MaximumScalarValues)
            throw new TextSearchQueryTooLongException();
        var escaped = Uri.EscapeDataString(text);
        return providerId.ToLowerInvariant() switch
        {
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
