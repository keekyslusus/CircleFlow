using System.Drawing;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CircleToSearch.TextRecognition;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class SelectionTextRefinerTests
{
    [Fact]
    public async Task Russian_candidate_replaces_lookalike_english_text()
    {
        var fake = new FakeLanguageRecognizer((source, tag, _) => Task.FromResult(
            OcrRecognitionOutcome.Success(Document(source, tag,
                tag.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? "добавить уведомление" : "A06aB\"TS YBeAomneHHe"))));
        var selection = Selection("preliminary", new Rectangle(20, 20, 80, 12));

        var outcome = await Refiner(fake).RefineAsync(Frame(), selection, CancellationToken.None);

        Assert.Equal(SelectionTextRefinementStatus.Success, outcome.Status);
        Assert.Equal("добавить уведомление", outcome.Text);
        Assert.All(fake.Sizes, size => Assert.True(size.Height >= 39));
    }

    [Fact]
    public async Task Failed_second_line_falls_back_only_for_that_line_and_preserves_newline()
    {
        var fake = new FakeLanguageRecognizer((source, tag, _) => Task.FromResult(
            source.PixelWidth < 170
                ? OcrRecognitionOutcome.Success(Document(source, tag, "уточнено"))
                : OcrRecognitionOutcome.Failed()));
        var selection = Selection(
            ("first", new Rectangle(20, 20, 40, 12), "ru-RU"),
            ("second preliminary", new Rectangle(20, 60, 60, 12), "en-US"));

        var outcome = await Refiner(fake).RefineAsync(Frame(), selection, CancellationToken.None);

        Assert.Equal(SelectionTextRefinementStatus.Success, outcome.Status);
        Assert.Equal($"уточнено{Environment.NewLine}second preliminary", outcome.Text);
    }

    [Fact]
    public async Task One_failed_language_pass_does_not_replace_preliminary_line_with_other_pass_garbage()
    {
        var fake = new FakeLanguageRecognizer((source, tag, _) => Task.FromResult(
            source.PixelWidth < 170
                ? tag.StartsWith("ru", StringComparison.OrdinalIgnoreCase)
                    ? OcrRecognitionOutcome.Failed()
                    : OcrRecognitionOutcome.Success(Document(source, tag, "A06aBVlTb"))
                : OcrRecognitionOutcome.Success(Document(source, tag, "refined second"))));
        var selection = Selection(
            ("добавить", new Rectangle(20, 20, 40, 12), "ru-RU"),
            ("second preliminary", new Rectangle(20, 60, 60, 12), "en-US"));

        var outcome = await Refiner(fake).RefineAsync(Frame(), selection, CancellationToken.None);

        Assert.Equal(SelectionTextRefinementStatus.Success, outcome.Status);
        Assert.Equal($"добавить{Environment.NewLine}refined second", outcome.Text);
    }

    [Fact]
    public async Task No_text_from_russian_pass_does_not_replace_cyrillic_preliminary_with_english_garbage()
    {
        var fake = new FakeLanguageRecognizer((source, tag, _) => Task.FromResult(
            tag.StartsWith("ru", StringComparison.OrdinalIgnoreCase)
                ? OcrRecognitionOutcome.NoText()
                : OcrRecognitionOutcome.Success(Document(source, tag, "A06aBVlTb"))));

        var outcome = await Refiner(fake).RefineAsync(
            Frame(), Selection("добавить", new Rectangle(20, 20, 60, 12)), CancellationToken.None);

        Assert.Equal(SelectionTextRefinementStatus.NoText, outcome.Status);
        Assert.Null(outcome.Text);
    }

    [Fact]
    public async Task No_text_from_russian_pass_still_allows_matching_english_refinement()
    {
        var fake = new FakeLanguageRecognizer((source, tag, _) => Task.FromResult(
            tag.StartsWith("ru", StringComparison.OrdinalIgnoreCase)
                ? OcrRecognitionOutcome.NoText()
                : OcrRecognitionOutcome.Success(Document(source, tag, "Add notification"))));

        var outcome = await Refiner(fake).RefineAsync(
            Frame(), Selection("Add notification", new Rectangle(20, 20, 100, 12)), CancellationToken.None);

        Assert.Equal(SelectionTextRefinementStatus.Success, outcome.Status);
        Assert.Equal("Add notification", outcome.Text);
    }

    [Fact]
    public async Task Cancellation_never_returns_success()
    {
        var fake = new FakeLanguageRecognizer(async (_, _, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return OcrRecognitionOutcome.NoText();
        });
        using var cancellation = new CancellationTokenSource();
        var task = Refiner(fake).RefineAsync(Frame(), Selection("text", new Rectangle(10, 10, 40, 12)), cancellation.Token);
        cancellation.Cancel();

        Assert.Equal(SelectionTextRefinementStatus.Canceled, (await task).Status);
    }

    [Fact]
    public async Task Padding_does_not_add_a_neighbor_whose_center_is_outside_selection()
    {
        var fake = new FakeLanguageRecognizer((source, tag, _) =>
        {
            var selected = new OcrWord(0, 0, 0, tag, "selected", new Rectangle(10, 8, 100, 30));
            var neighbor = new OcrWord(1, 0, 1, tag, "neighbor", new Rectangle(source.PixelWidth - 5, 8, 5, 30));
            var line = new OcrLine(0, 0, tag, Rectangle.Union(selected.BoundsPx, neighbor.BoundsPx), [selected, neighbor]);
            return Task.FromResult(OcrRecognitionOutcome.Success(new OcrDocument(
                tag, new Size(source.PixelWidth, source.PixelHeight), [line])));
        });

        var outcome = await Refiner(fake).RefineAsync(
            Frame(), Selection("preliminary", new Rectangle(20, 20, 40, 12)), CancellationToken.None);

        Assert.Equal("selected", outcome.Text);
    }

    [Theory]
    [InlineData(10, 3)]
    [InlineData(20, 2)]
    [InlineData(50, 1)]
    public void Adaptive_scale_targets_readable_height(int height, double expected) =>
        Assert.Equal(expected, SelectionTextRefiner.CalculateScale(height, new Rectangle(0, 0, 100, height + 4), 2600), 3);

    [Fact]
    public void Crop_is_padded_and_clamped_on_every_frame_edge()
    {
        Assert.Equal(new Rectangle(0, 0, 15, 15),
            SelectionTextRefiner.CreateCropBounds(new Rectangle(0, 0, 12, 12), 100, 100));
        Assert.Equal(Rectangle.FromLTRB(85, 85, 100, 100),
            SelectionTextRefiner.CreateCropBounds(new Rectangle(88, 88, 12, 12), 100, 100));
    }

    private static SelectionTextRefiner Refiner(ILanguageOcrRecognizer recognizer) => new(
        recognizer,
        new OcrLanguageCatalog([
            new OcrLanguageOption("ru-RU", "Russian"),
            new OcrLanguageOption("en-US", "English")]),
        new AutomaticOcrLanguageResolver(),
        new OcrDocumentMerger(new OcrTextQualityScorer()),
        2600);

    private static BitmapSource Frame() => BitmapSource.Create(
        300, 120, 96, 96, PixelFormats.Bgra32, null, new byte[144000], 1200);

    private static OcrDocument Document(BitmapSource source, string language, string text)
    {
        var bounds = new Rectangle(2, 2, Math.Max(1, source.PixelWidth - 4), Math.Max(1, source.PixelHeight - 4));
        var word = new OcrWord(0, 0, 0, language, text, bounds);
        return new OcrDocument(language, new Size(source.PixelWidth, source.PixelHeight),
            [new OcrLine(0, 0, language, bounds, [word])]);
    }

    private static TextSelectionRange Selection(string text, Rectangle bounds) => Selection((text, bounds, "en-US"));

    private static TextSelectionRange Selection(params (string Text, Rectangle Bounds, string Language)[] lines)
    {
        var words = lines.Select((line, index) => new OcrWord(index, index, index, line.Language, line.Text, line.Bounds)).ToArray();
        var document = new OcrDocument(lines[0].Language, new Size(300, 120), lines.Select((line, index) =>
            new OcrLine(index, index, line.Language, line.Bounds, [words[index]])).ToArray());
        return TextSelectionRange.Create(document, words[0], words[^1]);
    }

    private sealed class FakeLanguageRecognizer(
        Func<BitmapSource, string, CancellationToken, Task<OcrRecognitionOutcome>> recognize) : ILanguageOcrRecognizer
    {
        internal List<Size> Sizes { get; } = [];

        public Task<OcrRecognitionOutcome> RecognizeAsync(
            BitmapSource source,
            string languageTag,
            CancellationToken cancellationToken)
        {
            Sizes.Add(new Size(source.PixelWidth, source.PixelHeight));
            return recognize(source, languageTag, cancellationToken);
        }
    }
}
