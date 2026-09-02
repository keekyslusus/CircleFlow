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

    private static OcrDocument Document()
    {
        var first = new OcrWord(0, 0, 0, "Hello", new Rectangle(0, 0, 20, 10));
        var second = new OcrWord(1, 1, 1, "World", new Rectangle(0, 20, 20, 10));
        return new OcrDocument("en-US", new Size(100, 100),
        [
            new OcrLine(0, 0, first.BoundsPx, [first]),
            new OcrLine(1, 1, second.BoundsPx, [second]),
        ]);
    }

    private sealed class FakeProvider : ITranslationProvider
    {
        private readonly Func<IReadOnlyList<TranslationChunk>, TranslationBatchOutcome> _translate;

        internal FakeProvider(Func<IReadOnlyList<TranslationChunk>, TranslationBatchOutcome> translate) =>
            _translate = translate;

        internal FakeProvider(IReadOnlyList<TranslatedChunk> chunks) : this(_ => new TranslationBatchOutcome(chunks)) { }

        internal int Calls { get; private set; }

        public Task<TranslationBatchOutcome> TranslateAsync(
            IReadOnlyList<TranslationChunk> chunks,
            string sourceLanguageTag,
            string targetLanguageTag,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_translate(chunks));
        }
    }
}
