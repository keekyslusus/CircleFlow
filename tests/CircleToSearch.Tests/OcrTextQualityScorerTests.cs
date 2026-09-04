using CircleToSearch.TextRecognition;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class OcrTextQualityScorerTests
{
    private readonly OcrTextQualityScorer _scorer = new();

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
}
