using CircleToSearch.Search;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class YandexResultUrlPolicyTests
{
    [Theory]
    [InlineData("https://yandex.ru/images/search?rpt=imageview&url=https%3A%2F%2Favatars.mds.yandex.net%2Fx")]
    [InlineData("https://yandex.kz/images/search?rpt=imageview")]
    [InlineData("https://yandex.com/images/search?rpt=imageview")]
    public void Yandex_https_results_are_allowed(string url)
    {
        Assert.True(YandexResultUrlPolicy.IsAllowed(new Uri(url)));
    }

    [Theory]
    [InlineData("http://yandex.ru/images/search")]
    [InlineData("https://yandex.ru.evil.test/images/search")]
    [InlineData("https://notyandex.ru/images/search")]
    [InlineData("https://avatars.mds.yandex.net/get-images-cbir/x/orig")]
    public void Other_locations_are_rejected(string url)
    {
        Assert.False(YandexResultUrlPolicy.IsAllowed(new Uri(url)));
    }

    [Fact]
    public void Relative_and_missing_locations_are_rejected()
    {
        Assert.False(YandexResultUrlPolicy.IsAllowed(null));
        Assert.False(YandexResultUrlPolicy.IsAllowed(new Uri("/search", UriKind.Relative)));
    }
}
