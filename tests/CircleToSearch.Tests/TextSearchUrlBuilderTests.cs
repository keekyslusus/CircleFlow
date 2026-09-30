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

    [Theory]
    [InlineData("bing", "https://www.bing.com/search?q=")]
    [InlineData("duckduckgo", "https://duckduckgo.com/?q=")]
    [InlineData("google", "https://www.google.com/search?q=")]
    [InlineData("kagi", "https://kagi.com/search?q=")]
    [InlineData("qwant", "https://www.qwant.com/?q=")]
    [InlineData("startpage", "https://www.startpage.com/sp/search?q=")]
    public void Selected_engine_replaces_the_placeholder_regardless_of_image_provider(string engine, string prefix)
    {
        var url = new TextSearchUrlBuilder().Build("a&b %s 世界", SearchProviderIds.YandexImages, engine);

        Assert.Equal(prefix + "a%26b%20%25s%20%E4%B8%96%E7%95%8C", url);
    }

    [Fact]
    public void Engines_are_alphabetical_and_unknown_engine_is_rejected()
    {
        var ids = TextSearchEngines.All.Select(engine => engine.Id).ToArray();

        Assert.Equal(ids.Order(StringComparer.Ordinal), ids);
        Assert.All(TextSearchEngines.All, engine => Assert.Contains(TextSearchEngines.QueryPlaceholder, engine.UrlTemplate));
        Assert.Throws<ArgumentException>(() => new TextSearchUrlBuilder().Build("text", SearchProviderIds.GoogleLens, "yandex"));
    }

    [Theory]
    [InlineData(SearchProviderIds.GoogleLens, "", "https://www.google.com/", "google.com")]
    [InlineData(SearchProviderIds.TraceMoe, "", "https://anilist.co/", "anilist.co")]
    [InlineData(SearchProviderIds.Pinterest, "", "https://www.google.com/", "google.com")]
    [InlineData(SearchProviderIds.YandexImages, "startpage", "https://www.startpage.com/", "startpage.com")]
    public void Origin_and_site_name_come_from_the_search_address_without_a_query(
        string provider, string engine, string origin, string site)
    {
        var builder = new TextSearchUrlBuilder();

        Assert.Equal(new Uri(origin), builder.Origin(provider, engine));
        Assert.Equal(site, builder.SiteName(provider, engine));
        Assert.Throws<ArgumentException>(() => builder.Origin("unknown"));
    }

    [Fact]
    public void Rejects_query_over_scalar_limit_without_splitting_surrogates()
    {
        var builder = new TextSearchUrlBuilder(2);

        Assert.Equal("https://www.google.com/search?q=%F0%9F%98%80a", builder.Build("😀a", SearchProviderIds.GoogleLens));
        Assert.Throws<TextSearchQueryTooLongException>(() => builder.Build("😀ab", SearchProviderIds.GoogleLens));
    }
}
