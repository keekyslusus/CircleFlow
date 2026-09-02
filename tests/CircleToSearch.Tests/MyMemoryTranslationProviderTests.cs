using System.Net;
using System.Net.Http;
using CircleToSearch.Translation;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class MyMemoryTranslationProviderTests
{
    [Fact]
    public async Task Uses_exact_https_host_langpair_and_escaped_query()
    {
        Uri? captured = null;
        using var client = new HttpClient(new DelegateHandler((request, _) =>
        {
            captured = request.RequestUri;
            return Task.FromResult(Json("translated"));
        }));
        var provider = new MyMemoryTranslationProvider(client);

        var result = await provider.TranslateAsync(
            [new TranslationChunk(0, 0, "a&b 世界")], "en-US", "ru-RU", CancellationToken.None);

        Assert.True(result.HasSuccess);
        Assert.Equal("https", captured!.Scheme);
        Assert.Equal("api.mymemory.translated.net", captured.Host);
        Assert.Contains("q=a%26b%20%E4%B8%96%E7%95%8C", captured.Query);
        Assert.Contains("langpair=en-us%7Cru-ru", captured.Query);
        Assert.DoesNotContain("key=", captured.Query);
    }

    [Fact]
    public async Task Deduplicates_chunks_and_preserves_output_order()
    {
        var calls = 0;
        using var client = new HttpClient(new DelegateHandler((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(Json("ok"));
        }));
        var provider = new MyMemoryTranslationProvider(client);

        var result = await provider.TranslateAsync(
            [new TranslationChunk(0, 0, "same"), new TranslationChunk(1, 0, "same")],
            "en", "de", CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.Equal([0, 1], result.Chunks.Select(chunk => chunk.LineId));
    }

    [Fact]
    public async Task Enforces_two_request_concurrency()
    {
        var active = 0;
        var maximum = 0;
        using var client = new HttpClient(new DelegateHandler(async (_, cancellationToken) =>
        {
            var current = Interlocked.Increment(ref active);
            maximum = Math.Max(maximum, current);
            await Task.Delay(30, cancellationToken);
            Interlocked.Decrement(ref active);
            return Json("ok");
        }));
        var provider = new MyMemoryTranslationProvider(client);

        var result = await provider.TranslateAsync(
            Enumerable.Range(0, 6).Select(index => new TranslationChunk(index, 0, "text" + index)).ToArray(),
            "en", "fr", CancellationToken.None);

        Assert.True(result.HasSuccess);
        Assert.Equal(2, maximum);
    }

    [Fact]
    public async Task Regional_language_pair_retries_once_with_neutral_tags_when_rejected()
    {
        var uris = new List<Uri>();
        using var client = new HttpClient(new DelegateHandler((request, _) =>
        {
            uris.Add(request.RequestUri!);
            return Task.FromResult(uris.Count == 1
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"responseStatus\":400}") }
                : Json("ok"));
        }));

        var result = await new MyMemoryTranslationProvider(client).TranslateAsync(
            [new TranslationChunk(0, 0, "source")], "en-US", "pt-BR", CancellationToken.None);

        Assert.True(result.HasSuccess);
        Assert.Contains("langpair=en-us%7Cpt-br", uris[0].Query);
        Assert.Contains("langpair=en%7Cpt", uris[1].Query);
    }

    [Theory]
    [InlineData("not json", TranslationFailure.BadResponse)]
    [InlineData("{\"responseStatus\":429}", TranslationFailure.RateLimited)]
    public async Task Classifies_invalid_and_service_responses(string body, TranslationFailure expected)
    {
        using var client = new HttpClient(new DelegateHandler((_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) })));
        var provider = new MyMemoryTranslationProvider(client);

        var result = await provider.TranslateAsync(
            [new TranslationChunk(0, 0, "source")], "en", "es", CancellationToken.None);

        Assert.Equal(expected, Assert.Single(result.Chunks).Failure);
    }

    [Fact]
    public async Task Timeout_and_caller_cancellation_are_distinct()
    {
        using var client = new HttpClient(new DelegateHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Json("never");
        }));
        var provider = new MyMemoryTranslationProvider(client, TimeSpan.FromMilliseconds(20));
        var timedOut = await provider.TranslateAsync(
            [new TranslationChunk(0, 0, "source")], "en", "es", CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var canceled = await provider.TranslateAsync(
            [new TranslationChunk(0, 0, "source")], "en", "es", cancellation.Token);

        Assert.Equal(TranslationFailure.Timeout, Assert.Single(timedOut.Chunks).Failure);
        Assert.Equal(TranslationFailure.Canceled, Assert.Single(canceled.Chunks).Failure);
    }

    private static HttpResponseMessage Json(string translated) => new(HttpStatusCode.OK)
    {
        Content = new StringContent($"{{\"responseStatus\":200,\"responseData\":{{\"translatedText\":\"{translated}\"}}}}"),
    };

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
