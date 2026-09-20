using CircleToSearch.TextRecognition;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class OcrLanguageCatalogResolutionTests
{
    [Fact]
    public void Resolution_prefers_exact_tag_then_deterministic_compatible_variant()
    {
        var catalog = new OcrLanguageCatalog([
            new("en-ZA", "Zulu order"), new("en-US", "American English"),
            new("en-GB", "British English"), new("zh-Hant-TW", "Traditional"),
            new("zh-Hans-CN", "Simplified"),
        ]);
        Assert.Equal("en-GB", catalog.Resolve("EN-gb")?.Tag);
        Assert.Equal("en-GB", catalog.Resolve("en-AU")?.Tag);
        Assert.Equal("zh-Hant-TW", catalog.Resolve("zh-Hant-HK")?.Tag);
        Assert.Equal("zh-Hans-CN", catalog.Resolve("zh-Hans-SG")?.Tag);
        Assert.Equal("zh-Hant-TW", catalog.Resolve("zh-HK")?.Tag);
        Assert.Equal("zh-Hans-CN", catalog.Resolve("zh-CN")?.Tag);
        Assert.Null(catalog.Resolve("zh-JP"));
        Assert.Null(catalog.Resolve("ru-RU"));
        Assert.Null(new OcrLanguageCatalog([]).Resolve("en-US"));
    }
}
