using System.Globalization;
using CircleToSearch.Translation;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class TranslationLanguagesTests
{
    [Theory]
    [InlineData("no", "nb")]
    [InlineData("SR", "sr-Cyrl")]
    [InlineData("iw", "he")]
    [InlineData("ja", "ja")]
    [InlineData("ru-RU", "ru-RU")]
    public void Recognition_uses_the_standard_tag_for_google_codes(string code, string expected) =>
        Assert.Equal(expected, TranslationLanguages.RecognitionTag(code));

    [Fact]
    public void Kurmanji_keeps_its_own_name_instead_of_the_windows_name_for_ku() =>
        Assert.Equal("Kurdish (Kurmanji)", TranslationLanguages.DisplayName("ku", TestUiStrings.English));

    [Fact]
    public void System_language_is_named_like_the_same_language_in_the_list() =>
        Assert.Equal(TranslationLanguages.DisplayName("de", TestUiStrings.English),
            TranslationTargetLanguage.DisplayName(CultureInfo.GetCultureInfo("de-AT").Name, TestUiStrings.English));

    [Fact]
    public void Listed_codes_cannot_be_changed_from_outside() =>
        Assert.IsNotType<string[]>(TranslationLanguages.AllCodes);
}
