using CircleToSearch.TextRecognition;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class OcrUnicodeScriptClassifierTests
{
    private readonly OcrUnicodeScriptClassifier _classifier = new();

    [Theory]
    [InlineData("English text")]
    [InlineData("größere Straße")]
    [InlineData("français déjà")]
    public void Latin_text_is_classified(string text) => AssertOnly(text, OcrUnicodeScript.Latin);

    [Theory]
    [InlineData("русский текст")]
    [InlineData("український текст")]
    [InlineData("български текст")]
    public void Cyrillic_text_is_classified(string text) => AssertOnly(text, OcrUnicodeScript.Cyrillic);

    [Theory]
    [InlineData("Ελληνικό κείμενο", OcrUnicodeScript.Greek)]
    [InlineData("النص العربي", OcrUnicodeScript.Arabic)]
    [InlineData("טקסט עברי", OcrUnicodeScript.Hebrew)]
    [InlineData("हिन्दी पाठ", OcrUnicodeScript.Devanagari)]
    [InlineData("한국어 텍스트", OcrUnicodeScript.Hangul)]
    [InlineData("ข้อความไทย", OcrUnicodeScript.Thai)]
    public void Supported_non_latin_scripts_are_classified(string text, OcrUnicodeScript script) => AssertOnly(text, script);

    [Fact]
    public void Japanese_reports_both_han_and_kana()
    {
        var result = _classifier.Analyze("日本語のテキスト");

        Assert.Contains(OcrUnicodeScript.Han, result.SignificantScripts);
        Assert.Contains(OcrUnicodeScript.HiraganaKatakana, result.SignificantScripts);
    }

    [Fact]
    public void Supplementary_han_is_read_as_a_rune() => AssertOnly("𠀀漢字", OcrUnicodeScript.Han);

    [Fact]
    public void Punctuation_digits_whitespace_and_emoji_are_not_scripts()
    {
        var result = _classifier.Analyze("123 — ... 😀");

        Assert.Equal(0, result.LetterCount);
        Assert.Null(result.DominantScript);
        Assert.Empty(result.SignificantScripts);
    }

    [Fact]
    public void A_single_accidental_letter_is_not_significant_in_longer_text()
    {
        var result = _classifier.Analyze("кириллицаещёx");

        Assert.Contains(OcrUnicodeScript.Cyrillic, result.SignificantScripts);
        Assert.DoesNotContain(OcrUnicodeScript.Latin, result.SignificantScripts);
    }

    private void AssertOnly(string text, OcrUnicodeScript script)
    {
        var result = _classifier.Analyze(text);

        Assert.Equal(script, result.DominantScript);
        Assert.Equal([script], result.SignificantScripts);
        Assert.Equal(result.LetterCount, result.Count(script));
    }
}
