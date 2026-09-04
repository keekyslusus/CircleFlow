using System.Drawing;
using CircleToSearch.TextRecognition;
using CircleToSearch.Translation;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class ScreenTranslationWorkflowTests
{
    [Fact]
    public async Task Same_language_avoids_provider_request()
    {
        var provider = new FakeProvider([]);
        var workflow = new ScreenTranslationWorkflow(provider, new TranslationSegmenter());

        var outcome = await workflow.TranslateAsync(Guid.NewGuid(), Document(), "en-GB", CancellationToken.None);

        Assert.Equal(TranslationFailure.SameLanguage, outcome.Failure);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Partial_failure_returns_successful_lines_and_partial_flag()
    {
        var provider = new FakeProvider(chunks => new TranslationBatchOutcome(chunks.Select(chunk =>
            chunk.LineId == 0
                ? new TranslatedChunk(chunk.LineId, chunk.Order, chunk.Text, "Hola", TranslationFailure.None)
                : new TranslatedChunk(chunk.LineId, chunk.Order, chunk.Text, null, TranslationFailure.Network)).ToArray()));
        var requestId = Guid.NewGuid();
        var outcome = await new ScreenTranslationWorkflow(provider, new TranslationSegmenter())
            .TranslateAsync(requestId, Document(), "es", CancellationToken.None);

        Assert.True(outcome.Success);
        Assert.True(outcome.Result!.IsPartial);
        Assert.Equal(requestId, outcome.Result.RequestId);
        Assert.Equal("Hola", Assert.Single(outcome.Result.Lines).TranslatedText);
    }

    [Fact]
    public async Task Mixed_language_lines_are_sent_in_source_language_groups()
    {
        var provider = new FakeProvider(chunks => new TranslationBatchOutcome(chunks.Select(chunk =>
            new TranslatedChunk(chunk.LineId, chunk.Order, chunk.Text, chunk.Text + "!", TranslationFailure.None)).ToArray()));
        var document = MixedDocument("ru-RU", "Привет", "en-US", "Hello");

        var outcome = await new ScreenTranslationWorkflow(provider, new TranslationSegmenter())
            .TranslateAsync(Guid.NewGuid(), document, "es", CancellationToken.None);

        Assert.True(outcome.Success);
        Assert.False(outcome.Result!.IsPartial);
        Assert.Equal(["ru-RU", "en-US"], provider.SourceTags);
        Assert.Equal(2, outcome.Result.Lines.Count);
    }

    [Fact]
    public async Task Target_equivalent_lines_are_skipped_without_causing_partial_failure()
    {
        var provider = new FakeProvider(chunks => new TranslationBatchOutcome(chunks.Select(chunk =>
            new TranslatedChunk(chunk.LineId, chunk.Order, chunk.Text, "Hello", TranslationFailure.None)).ToArray()));
        var document = MixedDocument("ru-RU", "Привет", "en-GB", "Already English");

        var outcome = await new ScreenTranslationWorkflow(provider, new TranslationSegmenter())
            .TranslateAsync(Guid.NewGuid(), document, "en-US", CancellationToken.None);

        Assert.True(outcome.Success);
        Assert.False(outcome.Result!.IsPartial);
        Assert.Equal(["ru-RU"], provider.SourceTags);
        Assert.Single(outcome.Result.Lines);
    }

    [Fact]
    public async Task Mixed_line_translates_only_non_target_language_run()
    {
        var provider = new FakeProvider(chunks => new TranslationBatchOutcome(chunks.Select(chunk =>
            new TranslatedChunk(chunk.LineId, chunk.Order, chunk.Text, "add", TranslationFailure.None)).ToArray()));
        var words = new[]
        {
            new OcrWord(0, 0, 0, "en-US", "Telegram", new Rectangle(0, 0, 70, 20)),
            new OcrWord(1, 0, 1, "ru-RU", "—", new Rectangle(75, 0, 10, 20)),
            new OcrWord(2, 0, 2, "ru-RU", "добавить", new Rectangle(90, 0, 90, 20)),
            new OcrWord(3, 0, 3, "en-US", "notification", new Rectangle(185, 0, 110, 20)),
        };
        var line = new OcrLine(0, 0, "en-US", new Rectangle(0, 0, 295, 20), words);
        var document = new OcrDocument("en-US", new Size(300, 30), [line]);

        var outcome = await new ScreenTranslationWorkflow(provider, new TranslationSegmenter())
            .TranslateAsync(Guid.NewGuid(), document, "en-GB", CancellationToken.None);

        Assert.True(outcome.Success);
        Assert.Equal(["ru-RU"], provider.SourceTags);
        Assert.Equal(["добавить"], Assert.Single(provider.Requests).Chunks.Select(chunk => chunk.Text));
        Assert.Equal("Telegram — add notification", Assert.Single(outcome.Result!.Lines).TranslatedText);
    }

    [Fact]
    public async Task Mixed_line_is_split_by_language_and_reassembled_in_original_order()
    {
        var provider = new FakeProvider(chunks => new TranslationBatchOutcome(chunks.Select(chunk =>
        {
            var translated = chunk.Text switch
            {
                "Telegram —" => "Telegram —",
                "добавить" => "agregar",
                "notification" => "notificación",
                _ => throw new InvalidOperationException(chunk.Text),
            };
            return new TranslatedChunk(chunk.LineId, chunk.Order, chunk.Text, translated, TranslationFailure.None);
        }).ToArray()));
        var words = new[]
        {
            new OcrWord(0, 0, 0, "en-US", "Telegram", new Rectangle(0, 0, 70, 20)),
            new OcrWord(1, 0, 1, "ru-RU", "—", new Rectangle(75, 0, 10, 20)),
            new OcrWord(2, 0, 2, "ru-RU", "добавить", new Rectangle(90, 0, 90, 20)),
            new OcrWord(3, 0, 3, "en-US", "notification", new Rectangle(185, 0, 110, 20)),
        };
        var document = new OcrDocument("en-US", new Size(300, 30),
            [new OcrLine(0, 0, "en-US", new Rectangle(0, 0, 295, 20), words)]);

        var outcome = await new ScreenTranslationWorkflow(provider, new TranslationSegmenter())
            .TranslateAsync(Guid.NewGuid(), document, "es", CancellationToken.None);

        Assert.True(outcome.Success);
        Assert.Equal("Telegram — agregar notificación", Assert.Single(outcome.Result!.Lines).TranslatedText);
        Assert.Equal(["en-US", "ru-RU"], provider.SourceTags);
        Assert.Equal(["Telegram —", "notification"], provider.Requests[0].Chunks.Select(chunk => chunk.Text));
        Assert.Equal(["добавить"], provider.Requests[1].Chunks.Select(chunk => chunk.Text));
    }

    [Fact]
    public async Task Punctuation_tag_from_other_ocr_pass_does_not_make_target_language_line_translatable()
    {
        var provider = new FakeProvider([]);
        var words = new[]
        {
            new OcrWord(0, 0, 0, "en-US", "Telegram", new Rectangle(0, 0, 70, 20)),
            new OcrWord(1, 0, 1, "ru-RU", "—", new Rectangle(75, 0, 10, 20)),
            new OcrWord(2, 0, 2, "en-US", "notification", new Rectangle(90, 0, 110, 20)),
        };
        var document = new OcrDocument("en-US", new Size(210, 30),
            [new OcrLine(0, 0, "en-US", new Rectangle(0, 0, 200, 20), words)]);

        var outcome = await new ScreenTranslationWorkflow(provider, new TranslationSegmenter())
            .TranslateAsync(Guid.NewGuid(), document, "en-GB", CancellationToken.None);

        Assert.Equal(TranslationFailure.SameLanguage, outcome.Failure);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Pre_canceled_request_returns_canceled_outcome_without_provider_call()
    {
        var provider = new FakeProvider([]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var outcome = await new ScreenTranslationWorkflow(provider, new TranslationSegmenter())
            .TranslateAsync(Guid.NewGuid(), Document(), "es", cancellation.Token);

        Assert.Equal(TranslationFailure.Canceled, outcome.Failure);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task Provider_canceled_result_returns_canceled_outcome()
    {
        var provider = new FakeProvider(chunks => new TranslationBatchOutcome(chunks.Select(chunk =>
            new TranslatedChunk(chunk.LineId, chunk.Order, chunk.Text, null, TranslationFailure.Canceled)).ToArray()));

        var outcome = await new ScreenTranslationWorkflow(provider, new TranslationSegmenter())
            .TranslateAsync(Guid.NewGuid(), Document(), "es", CancellationToken.None);

        Assert.Equal(TranslationFailure.Canceled, outcome.Failure);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task Cancellation_between_language_groups_returns_canceled_outcome()
    {
        using var cancellation = new CancellationTokenSource();
        var provider = new FakeProvider(chunks => new TranslationBatchOutcome(chunks.Select(chunk =>
            new TranslatedChunk(chunk.LineId, chunk.Order, chunk.Text, chunk.Text, TranslationFailure.None)).ToArray()))
        {
            OnCall = call =>
            {
                if (call == 1) cancellation.Cancel();
            },
        };

        var outcome = await new ScreenTranslationWorkflow(provider, new TranslationSegmenter())
            .TranslateAsync(Guid.NewGuid(), MixedDocument("ru-RU", "Привет", "en-US", "Hello"), "es", cancellation.Token);

        Assert.Equal(TranslationFailure.Canceled, outcome.Failure);
        Assert.Equal(1, provider.Calls);
    }

    private static OcrDocument Document()
    {
        var first = new OcrWord(0, 0, 0, "en-US", "Hello", new Rectangle(0, 0, 20, 10));
        var second = new OcrWord(1, 1, 1, "en-US", "World", new Rectangle(0, 20, 20, 10));
        return new OcrDocument("en-US", new Size(100, 100),
        [
            new OcrLine(0, 0, "en-US", first.BoundsPx, [first]),
            new OcrLine(1, 1, "en-US", second.BoundsPx, [second]),
        ]);
    }

    private static OcrDocument MixedDocument(string firstLanguage, string firstText, string secondLanguage, string secondText)
    {
        var first = new OcrWord(0, 0, 0, firstLanguage, firstText, new Rectangle(0, 0, 100, 10));
        var second = new OcrWord(1, 1, 1, secondLanguage, secondText, new Rectangle(0, 20, 100, 10));
        return new OcrDocument(firstLanguage, new Size(200, 100),
        [
            new OcrLine(0, 0, firstLanguage, first.BoundsPx, [first]),
            new OcrLine(1, 1, secondLanguage, second.BoundsPx, [second]),
        ]);
    }

    private sealed class FakeProvider : ITranslationProvider
    {
        private readonly Func<IReadOnlyList<TranslationChunk>, TranslationBatchOutcome> _translate;

        internal FakeProvider(Func<IReadOnlyList<TranslationChunk>, TranslationBatchOutcome> translate) =>
            _translate = translate;

        internal FakeProvider(IReadOnlyList<TranslatedChunk> chunks) : this(_ => new TranslationBatchOutcome(chunks)) { }

        internal int Calls { get; private set; }
        internal List<string> SourceTags { get; } = [];
        internal List<(string SourceTag, IReadOnlyList<TranslationChunk> Chunks)> Requests { get; } = [];
        internal Action<int>? OnCall { get; init; }

        public Task<TranslationBatchOutcome> TranslateAsync(
            IReadOnlyList<TranslationChunk> chunks,
            string sourceLanguageTag,
            string targetLanguageTag,
            CancellationToken cancellationToken)
        {
            Calls++;
            SourceTags.Add(sourceLanguageTag);
            Requests.Add((sourceLanguageTag, chunks));
            OnCall?.Invoke(Calls);
            return Task.FromResult(_translate(chunks));
        }
    }
}
