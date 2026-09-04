using System.Drawing;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CircleToSearch.TextRecognition;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class AutomaticOcrRecognizerTests
{
    [Fact]
    public async Task Available_languages_are_both_used_and_partial_failure_keeps_success()
    {
        var languageRecognizer = new FakeLanguageRecognizer(tag => tag.StartsWith("ru", StringComparison.OrdinalIgnoreCase)
            ? OcrRecognitionOutcome.Success(Document("ru-RU", "текст"))
            : OcrRecognitionOutcome.Failed());
        var recognizer = Create(languageRecognizer, "en-US", "ru-RU", "fr-FR");

        var outcome = await recognizer.RecognizeAsync(Frame(), CancellationToken.None);

        Assert.Equal(OcrRecognitionStatus.Success, outcome.Status);
        Assert.Equal(["ru-RU", "en-US"], languageRecognizer.Tags);
        Assert.Equal("текст", Assert.Single(outcome.Document!.Lines).Text);
    }

    [Fact]
    public async Task No_supported_pack_returns_language_unavailable_without_calling_engine()
    {
        var languageRecognizer = new FakeLanguageRecognizer(_ => throw new InvalidOperationException());
        var outcome = await Create(languageRecognizer, "fr-FR").RecognizeAsync(Frame(), CancellationToken.None);

        Assert.Equal(OcrRecognitionStatus.LanguageUnavailable, outcome.Status);
        Assert.Empty(languageRecognizer.Tags);
    }

    private static AutomaticOcrRecognizer Create(FakeLanguageRecognizer recognizer, params string[] tags) => new(
        recognizer,
        new OcrLanguageCatalog(tags.Select(tag => new OcrLanguageOption(tag, tag)).ToArray()),
        new AutomaticOcrLanguageResolver(),
        new OcrDocumentMerger(new OcrTextQualityScorer()));

    private static BitmapSource Frame() => BitmapSource.Create(
        100, 50, 96, 96, PixelFormats.Bgra32, null, new byte[20000], 400);

    private static OcrDocument Document(string language, string text)
    {
        var bounds = new Rectangle(10, 10, 50, 20);
        var word = new OcrWord(0, 0, 0, language, text, bounds);
        return new OcrDocument(language, new Size(100, 50), [new OcrLine(0, 0, language, bounds, [word])]);
    }

    private sealed class FakeLanguageRecognizer(Func<string, OcrRecognitionOutcome> recognize) : ILanguageOcrRecognizer
    {
        internal List<string> Tags { get; } = [];

        public Task<OcrRecognitionOutcome> RecognizeAsync(
            BitmapSource source,
            string languageTag,
            CancellationToken cancellationToken)
        {
            Tags.Add(languageTag);
            return Task.FromResult(recognize(languageTag));
        }
    }
}
