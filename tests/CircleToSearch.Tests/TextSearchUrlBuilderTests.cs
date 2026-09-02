using CircleToSearch.Search;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class TextSearchUrlBuilderTests
{
    [Theory]
    [InlineData(SearchProviderIds.GoogleLens, "www.google.com", "q")]
    [InlineData(SearchProviderIds.YandexImages, "yandex.com", "text")]
    public void Builds_https_provider_url_with_single_encoding(string provider, string host, string parameter)
    {
        var url = new TextSearchUrlBuilder().Build("a&b ?# 世界", provider);
        var uri = new Uri(url);

        Assert.Equal(Uri.UriSchemeHttps, uri.Scheme);
        Assert.Equal(host, uri.Host);
        Assert.Contains(parameter + "=a%26b%20%3F%23%20%E4%B8%96%E7%95%8C", uri.Query);
        Assert.DoesNotContain("%2526", uri.Query);
    }

    [Fact]
    public void Rejects_query_over_scalar_limit_without_splitting_surrogates()
    {
        var builder = new TextSearchUrlBuilder(2);

        Assert.Equal("https://www.google.com/search?q=%F0%9F%98%80a", builder.Build("😀a", SearchProviderIds.GoogleLens));
        Assert.Throws<TextSearchQueryTooLongException>(() => builder.Build("😀ab", SearchProviderIds.GoogleLens));
    }
}
