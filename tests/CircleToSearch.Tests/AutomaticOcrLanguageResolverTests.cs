using CircleToSearch.TextRecognition;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class AutomaticOcrLanguageResolverTests
{
    [Fact]
    public void Exact_preferred_tags_win_and_order_is_russian_then_english()
    {
        var resolved = Resolve("en-GB", "RU-ru", "en-US", "ru-KZ");

        Assert.Equal(["RU-ru", "en-US"], resolved);
    }

    [Fact]
    public void Neutral_fallback_is_case_insensitive_and_deterministic()
    {
        var resolved = Resolve("EN-gb", "ru-UA", "ru-BY", "en-AU");

        Assert.Equal(["ru-BY", "en-AU"], resolved);
    }

    [Fact]
    public void Missing_packs_are_omitted_without_duplicates()
    {
        Assert.Equal(["en-US"], Resolve("en-US", "EN-us", "fr-FR"));
        Assert.Empty(Resolve("fr-FR", "de-DE"));
    }

    private static IReadOnlyList<string> Resolve(params string[] tags) =>
        new AutomaticOcrLanguageResolver().Resolve(tags.Select(tag => new OcrLanguageOption(tag, tag)).ToArray());
}
