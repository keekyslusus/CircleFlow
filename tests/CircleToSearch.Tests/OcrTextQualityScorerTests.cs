using CircleToSearch.TextRecognition;
using Xunit;
using System.Drawing;

namespace CircleToSearch.Tests;

public sealed class OcrTextQualityScorerTests
{
    private readonly OcrTextQualityScorer _scorer = new(new OcrUnicodeScriptClassifier());

    [Fact]
    public void Correct_cyrillic_scores_above_latin_lookalike_noise() =>
        Assert.True(_scorer.Score("добавить уведомление", "ru-RU").Score >
                    _scorer.Score("A06aB\"TS YBeAomneHHe", "en-US").Score);

    [Fact]
    public void Correct_english_has_native_script_quality() =>
        Assert.True(_scorer.Score("Add notification", "en-US").Score >
                    _scorer.Score("Адд нот1ф1сат1он", "ru-RU").Score);

    [Fact]
    public void Numeric_punctuation_and_identifiers_remain_usable()
    {
        Assert.True(_scorer.Score("123 — 456", "en-US").IsUseful);
        Assert.True(_scorer.Score("build42", "en-US").IsUseful);
        Assert.True(_scorer.Score("...", "ru-RU").IsUseful);
    }

    [Fact]
    public void Replacement_and_control_characters_are_penalized() =>
        Assert.True(_scorer.Score("normal text", "en-US").Score > _scorer.Score("normal\ufffd\u0001text", "en-US").Score);

    [Fact]
    public void Unknown_latin_language_does_not_use_the_english_profile()
    {
        Assert.True(_scorer.Score("the notification", "en-US").ProfileRatio > 0);
        Assert.Equal(0, _scorer.Score("the notification", "de-DE").ProfileRatio);
    }

    [Theory]
    [InlineData("größere Straße", "de-DE")]
    [InlineData("français déjà", "fr-FR")]
    [InlineData("український текст", "uk-UA")]
    [InlineData("Ελληνικό κείμενο", "el-GR")]
    [InlineData("النص العربي", "ar-SA")]
    [InlineData("日本語のテキスト", "ja-JP")]
    [InlineData("한국어 텍스트", "ko-KR")]
    public void Unprofiled_languages_remain_useful(string text, string languageTag) =>
        Assert.True(_scorer.Score(text, languageTag).IsUseful);

    [Fact]
    public void Equivalent_text_uses_language_order_when_generic_quality_is_equal()
    {
        var german = Line("de-DE", "identique", 0);
        var french = Line("fr-FR", "identique", 1);

        Assert.Same(french, _scorer.Choose([german, french], ["fr-FR", "de-DE"]));
    }

    [Fact]
    public void Mixed_digits_inside_words_are_penalized() =>
        Assert.True(_scorer.Score("notification", "de-DE").Score >
                    _scorer.Score("not1ficat1on", "de-DE").Score);

    private static OcrLine Line(string languageTag, string text, int id)
    {
        var bounds = new Rectangle(0, 0, 100, 20);
        var word = new OcrWord(id, id, id, languageTag, text, bounds);
        return new OcrLine(id, id, languageTag, bounds, [word]);
    }
}
