using CircleToSearch.Search;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class RedirectUrlPolicyTests
{
    [Theory]
    [InlineData("https://lens.google.com/v3/upload?udm=26&vsrid=abc")]
    [InlineData("https://www.google.com/search?q=object")]
    [InlineData("https://google.com/results")]
    public void Google_https_locations_are_allowed(string url)
    {
        Assert.True(RedirectUrlPolicy.IsAllowed(new Uri(url)));
    }

    [Theory]
    [InlineData("http://lens.google.com/v3/upload")]
    [InlineData("https://google.com.evil.test/results")]
    [InlineData("https://notgoogle.com/results")]
    [InlineData("https://evil.google.com.evil.test/results")]
    [InlineData("ftp://google.com/results")]
    public void Other_locations_are_rejected(string url)
    {
        Assert.False(RedirectUrlPolicy.IsAllowed(new Uri(url)));
    }

    [Fact]
    public void Relative_and_missing_locations_are_rejected()
    {
        Assert.False(RedirectUrlPolicy.IsAllowed(null));
        Assert.False(RedirectUrlPolicy.IsAllowed(new Uri("/results", UriKind.Relative)));
    }
}
