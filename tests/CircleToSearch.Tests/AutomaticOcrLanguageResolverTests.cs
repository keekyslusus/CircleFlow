using CircleToSearch.TextRecognition;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class AutomaticOcrLanguageResolverTests
{
    [Fact]
    public void Every_installed_language_is_returned_in_deterministic_order() =>
        Assert.Equal(["de-DE", "en-US", "fr-FR", "ru-RU", "uk-UA"],
            Resolve("uk-UA", "fr-FR", "ru-RU", "de-DE", "en-US"));

    [Theory]
    [InlineData("de-DE")]
    [InlineData("ja-JP")]
    public void A_single_non_russian_non_english_pack_is_supported(string tag) =>
        Assert.Equal([tag], Resolve(tag));

    [Fact]
    public void Empty_catalog_returns_an_empty_snapshot() => Assert.Empty(Resolve());

    [Fact]
    public void Exact_duplicates_are_trimmed_and_removed_case_insensitively() =>
        Assert.Equal(["EN-us", "fr-FR"], Resolve(" fr-FR ", "en-US", " EN-us ", "  "));

    [Fact]
    public void Regional_variants_remain_separate() =>
        Assert.Equal(["en-GB", "en-US", "zh-Hans", "zh-Hant"], Resolve("zh-Hant", "en-US", "zh-Hans", "en-GB"));

    [Fact]
    public void Input_order_does_not_change_result() =>
        Assert.Equal(Resolve("uk-UA", "de-DE", "en-US"), Resolve("en-US", "uk-UA", "de-DE"));

    private static IReadOnlyList<string> Resolve(params string[] tags) =>
        new AutomaticOcrLanguageResolver().Resolve(tags.Select(tag => new OcrLanguageOption(tag, tag)).ToArray());
}
