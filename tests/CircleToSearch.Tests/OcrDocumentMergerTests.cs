using System.Drawing;
using CircleToSearch.TextRecognition;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class OcrDocumentMergerTests
{
    private readonly OcrDocumentMerger _merger = new(new OcrTextQualityScorer(new OcrUnicodeScriptClassifier()));

    [Fact]
    public void Russian_native_text_wins_over_english_lookalike_result()
    {
        var merged = Merge(
            Document("ru-RU", Line("ru-RU", "добавить уведомление", new Rectangle(10, 10, 180, 20))),
            Document("en-US", Line("en-US", "A06aB\"TS YBeAomneHHe", new Rectangle(10, 10, 180, 20))));

        Assert.Equal("добавить уведомление", Assert.Single(merged.Lines).Text);
        Assert.Equal("ru-RU", merged.LanguageTag);
    }

    [Fact]
    public void English_profile_does_not_override_correct_german_text()
    {
        var merged = _merger.Merge(
            [
                Document("de-DE", Line("de-DE", "größere Straße", new Rectangle(10, 10, 180, 20))),
                Document("en-US", Line("en-US", "grossere Strasse", new Rectangle(10, 10, 180, 20))),
            ],
            ["de-DE", "en-US"]);

        Assert.Equal("größere Straße", Assert.Single(merged.Lines).Text);
        Assert.Equal("de-DE", merged.LanguageTag);
    }

    [Fact]
    public void Additional_unprofiled_language_does_not_disable_russian_mojibake_protection()
    {
        var bounds = new Rectangle(10, 10, 80, 20);
        var merged = _merger.Merge(
            [
                Document("ru-RU", Line("ru-RU", "тест", bounds)),
                Document("en-US", Line("en-US", "Tect", bounds)),
                Document("de-DE", Line("de-DE", "Tect", bounds)),
            ],
            ["de-DE", "en-US", "ru-RU"]);

        Assert.Equal("тест", Assert.Single(merged.Lines).Text);
        Assert.Equal("ru-RU", merged.LanguageTag);
    }

    [Fact]
    public void Correct_english_wins_and_identical_candidates_follow_language_order()
    {
        var merged = Merge(
            Document("ru-RU", Line("ru-RU", "Адд нот1ф1сат1он", new Rectangle(10, 10, 140, 20))),
            Document("en-US", Line("en-US", "Add notification", new Rectangle(10, 10, 140, 20))));
        Assert.Equal("Add notification", Assert.Single(merged.Lines).Text);

        merged = Merge(
            Document("ru-RU", Line("ru-RU", "Telegram 123", new Rectangle(10, 10, 120, 20))),
            Document("en-US", Line("en-US", "Telegram 123", new Rectangle(10, 10, 120, 20))));
        Assert.Equal("ru-RU", Assert.Single(merged.Lines).LanguageTag);
    }

    [Fact]
    public void Separate_regions_and_columns_are_preserved_with_stable_ids()
    {
        var merged = Merge(
            Document("ru-RU", Line("ru-RU", "Русский текст", new Rectangle(10, 10, 120, 20))),
            Document("en-US",
                Line("en-US", "English text", new Rectangle(10, 60, 120, 20), 0),
                Line("en-US", "Other column", new Rectangle(500, 10, 120, 20), 1)));

        Assert.Equal(3, merged.Lines.Count);
        Assert.Equal([0, 1, 2], merged.Lines.Select(line => line.Id));
        Assert.Equal(Enumerable.Range(0, merged.Words.Count), merged.Words.Select(word => word.ReadingOrder));
    }

    [Fact]
    public void Dominant_language_is_weighted_by_letters_and_numeric_lines_survive()
    {
        var merged = Merge(
            Document("ru-RU", Line("ru-RU", "Очень длинное русское предложение", new Rectangle(10, 40, 300, 20))),
            Document("en-US",
                Line("en-US", "OK", new Rectangle(10, 10, 30, 20), 0),
                Line("en-US", "123", new Rectangle(400, 10, 40, 20), 1),
                Line("en-US", "...", new Rectangle(500, 10, 40, 20), 2)));

        Assert.Equal("ru-RU", merged.LanguageTag);
        Assert.Contains(merged.Lines, line => line.Text == "123");
        Assert.Contains(merged.Lines, line => line.Text == "...");
    }

    [Fact]
    public void Conflicting_mixed_line_is_assembled_from_best_words()
    {
        var bounds = new Rectangle(10, 10, 280, 20);
        var merged = Merge(
            Document("ru-RU", WordLine("ru-RU", 0, bounds,
                ("добавить", new Rectangle(10, 10, 90, 20)),
                ("notification", new Rectangle(115, 10, 175, 20)))),
            Document("en-US", WordLine("en-US", 0, bounds,
                ("A06aBVlTb", new Rectangle(10, 10, 90, 20)),
                ("notification", new Rectangle(115, 10, 175, 20)))));

        var line = Assert.Single(merged.Lines);
        Assert.Equal("добавить notification", line.Text);
        Assert.Equal(["ru-RU", "ru-RU"], line.Words.Select(word => word.LanguageTag));
    }

    [Fact]
    public void Fragmented_mixed_row_is_rejoined_and_aligned_without_losing_cyrillic()
    {
        var merged = Merge(
            Document("ru-RU",
                WordLine("ru-RU", 0, new Rectangle(10, 10, 90, 20),
                    ("Telegram", new Rectangle(10, 10, 70, 20)),
                    ("—", new Rectangle(85, 10, 15, 20))),
                WordLine("ru-RU", 1, new Rectangle(120, 10, 290, 20),
                    ("добавить", new Rectangle(120, 10, 100, 20)),
                    ("notification", new Rectangle(235, 10, 175, 20)))),
            Document("en-US",
                WordLine("en-US", 0, new Rectangle(10, 10, 90, 20),
                    ("Telegram", new Rectangle(10, 10, 70, 20)),
                    ("—", new Rectangle(85, 10, 15, 20))),
                WordLine("en-US", 1, new Rectangle(120, 10, 290, 20),
                    ("A06aBVlTb", new Rectangle(120, 10, 100, 20)),
                    ("notification", new Rectangle(235, 10, 175, 20)))));

        var line = Assert.Single(merged.Lines);
        Assert.Equal("Telegram — добавить notification", line.Text);
        Assert.All(line.Words, word => Assert.Equal("ru-RU", word.LanguageTag));
    }

    [Fact]
    public void One_to_many_alignment_does_not_append_garbage_fragments()
    {
        var bounds = new Rectangle(10, 10, 200, 20);
        var merged = Merge(
            Document("ru-RU", WordLine("ru-RU", 0, bounds,
                ("уведомление", new Rectangle(10, 10, 200, 20)))),
            Document("en-US", WordLine("en-US", 0, bounds,
                ("YBeAom", new Rectangle(10, 10, 100, 20)),
                ("neHHe", new Rectangle(110, 10, 100, 20)))));

        var line = Assert.Single(merged.Lines);
        Assert.Equal("уведомление", line.Text);
        Assert.Single(line.Words);
        Assert.Equal("ru-RU", line.Words[0].LanguageTag);
    }

    [Fact]
    public void Many_to_one_alignment_keeps_the_correct_english_word_once()
    {
        var bounds = new Rectangle(10, 10, 200, 20);
        var merged = Merge(
            Document("ru-RU", WordLine("ru-RU", 0, bounds,
                ("нот1ф1", new Rectangle(10, 10, 100, 20)),
                ("сат1он", new Rectangle(110, 10, 100, 20)))),
            Document("en-US", WordLine("en-US", 0, bounds,
                ("notification", new Rectangle(10, 10, 200, 20)))));

        var line = Assert.Single(merged.Lines);
        Assert.Equal("notification", line.Text);
        Assert.Single(line.Words);
        Assert.Equal("en-US", line.Words[0].LanguageTag);
    }

    [Fact]
    public void Nearby_ui_labels_do_not_merge_transitively()
    {
        var merged = Merge(Document("en-US",
            WordLine("en-US", 0, new Rectangle(10, 10, 40, 20),
                ("File", new Rectangle(10, 10, 40, 20))),
            WordLine("en-US", 1, new Rectangle(80, 10, 40, 20),
                ("Edit", new Rectangle(80, 10, 40, 20))),
            WordLine("en-US", 2, new Rectangle(150, 10, 40, 20),
                ("View", new Rectangle(150, 10, 40, 20)))));

        Assert.Equal(["File", "Edit", "View"], merged.Lines.Select(line => line.Text));
    }

    private OcrDocument Merge(params OcrDocument[] documents) => _merger.Merge(documents, ["ru-RU", "en-US"]);

    private static OcrDocument Document(string language, params OcrLine[] lines) =>
        new(language, new Size(800, 200), lines);

    private static OcrLine Line(string language, string text, Rectangle bounds, int id = 0)
    {
        var values = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var wordWidth = Math.Max(1, bounds.Width / values.Length);
        var words = values.Select((value, index) => new OcrWord(index, id, index, language, value,
            new Rectangle(bounds.Left + index * wordWidth, bounds.Top, wordWidth, bounds.Height))).ToArray();
        return new OcrLine(id, id, language, bounds, words);
    }

    private static OcrLine WordLine(
        string language,
        int id,
        Rectangle bounds,
        params (string Text, Rectangle Bounds)[] words) => new(
            id,
            id,
            language,
            bounds,
            words.Select((word, index) => new OcrWord(index, id, index, language, word.Text, word.Bounds)).ToArray());
}
