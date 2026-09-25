using CircleToSearch.Search;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class SearchProviderDescriptorTests
{
    [Fact]
    public void Live_name_is_read_on_each_use_and_equality_compares_current_values()
    {
        var name = "Yandex Images";
        var live = new SearchProviderDescriptor(SearchProviderIds.YandexImages, () => name);
        Assert.Equal(new SearchProviderDescriptor(SearchProviderIds.YandexImages, "Yandex Images"), live);

        name = "Яндекс Картинки";
        Assert.Equal("Яндекс Картинки", live.DisplayName);
        Assert.Equal(new SearchProviderDescriptor(SearchProviderIds.YandexImages, "Яндекс Картинки"), live);
        Assert.NotEqual(new SearchProviderDescriptor(SearchProviderIds.YandexImages, "Yandex Images"), live);
    }
}
