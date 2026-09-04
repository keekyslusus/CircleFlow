using System.Collections.Concurrent;
using System.Drawing;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CircleToSearch.TextRecognition;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class AutomaticOcrRecognizerTests
{
    [Fact]
    public async Task Every_available_language_is_used_and_partial_failure_keeps_success()
    {
        var languageRecognizer = new FakeLanguageRecognizer((tag, _) => Task.FromResult(
            tag.Equals("uk-UA", StringComparison.OrdinalIgnoreCase)
                ? OcrRecognitionOutcome.Success(Document(tag, "текст"))
                : OcrRecognitionOutcome.Failed()));
        var recognizer = Create(languageRecognizer, "en-US", "uk-UA", "fr-FR", "ru-RU", "de-DE");

        var outcome = await recognizer.RecognizeAsync(Frame(), CancellationToken.None);

        Assert.Equal(OcrRecognitionStatus.Success, outcome.Status);
        Assert.Equal(["de-DE", "en-US", "fr-FR", "ru-RU", "uk-UA"], languageRecognizer.Tags.Order().ToArray());
        Assert.Equal("текст", Assert.Single(outcome.Document!.Lines).Text);
    }

    [Fact]
    public async Task A_single_non_russian_non_english_pack_can_succeed()
    {
        var languageRecognizer = new FakeLanguageRecognizer((tag, _) =>
            Task.FromResult(OcrRecognitionOutcome.Success(Document(tag, "日本語"))));

        var outcome = await Create(languageRecognizer, "ja-JP").RecognizeAsync(Frame(), CancellationToken.None);

        Assert.Equal(OcrRecognitionStatus.Success, outcome.Status);
        Assert.Equal(["ja-JP"], languageRecognizer.Tags);
        Assert.Equal("日本語", Assert.Single(outcome.Document!.Lines).Text);
    }

    [Fact]
    public async Task Empty_catalog_returns_language_unavailable_without_calling_engine()
    {
        var languageRecognizer = new FakeLanguageRecognizer((_, _) => throw new InvalidOperationException());
        var outcome = await Create(languageRecognizer).RecognizeAsync(Frame(), CancellationToken.None);

        Assert.Equal(OcrRecognitionStatus.LanguageUnavailable, outcome.Status);
        Assert.Empty(languageRecognizer.Tags);
    }

    [Fact]
    public async Task Cancellation_does_not_publish_a_late_successful_document()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var languageRecognizer = new FakeLanguageRecognizer(async (tag, _) =>
        {
            await release.Task;
            return OcrRecognitionOutcome.Success(Document(tag, "late"));
        });
        using var cancellation = new CancellationTokenSource();
        var task = Create(languageRecognizer, "de-DE", "en-US", "fr-FR")
            .RecognizeAsync(Frame(), cancellation.Token);

        cancellation.Cancel();
        release.SetResult();

        Assert.Equal(OcrRecognitionStatus.Canceled, (await task).Status);
    }

    private static AutomaticOcrRecognizer Create(FakeLanguageRecognizer recognizer, params string[] tags)
    {
        var classifier = new OcrUnicodeScriptClassifier();
        return new AutomaticOcrRecognizer(
            recognizer,
            new OcrLanguageCatalog(tags.Select(tag => new OcrLanguageOption(tag, tag)).ToArray()),
            new AutomaticOcrLanguageResolver(),
            new OcrDocumentMerger(new OcrTextQualityScorer(classifier)));
    }

    private static BitmapSource Frame() => BitmapSource.Create(
        100, 50, 96, 96, PixelFormats.Bgra32, null, new byte[20000], 400);

    private static OcrDocument Document(string language, string text)
    {
        var bounds = new Rectangle(10, 10, 50, 20);
        var word = new OcrWord(0, 0, 0, language, text, bounds);
        return new OcrDocument(language, new Size(100, 50), [new OcrLine(0, 0, language, bounds, [word])]);
    }

    private sealed class FakeLanguageRecognizer(
        Func<string, CancellationToken, Task<OcrRecognitionOutcome>> recognize) : ILanguageOcrRecognizer
    {
        private readonly ConcurrentQueue<string> _tags = new();
        internal IReadOnlyList<string> Tags => _tags.ToArray();

        public Task<OcrRecognitionOutcome> RecognizeAsync(
            BitmapSource source,
            string languageTag,
            CancellationToken cancellationToken)
        {
            _tags.Enqueue(languageTag);
            return recognize(languageTag, cancellationToken);
        }
    }
}
