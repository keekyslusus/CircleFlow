using System.Net;
using System.Net.Http;
using CircleToSearch.Search;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class TraceMoeProviderTests
{
    [Fact]
    public void Parses_supplied_response_and_selects_best_match()
    {
        var match = TraceMoeProvider.Parse(File.ReadAllText(Path.Combine(TestOutputPaths.RepoDirectory, "tests", "CircleToSearch.Tests", "Fixtures", "trace-moe.json")));
        Assert.NotNull(match);
        Assert.Equal("Mob Psycho 100 III", match.Title);
        Assert.Equal("1", match.Episode);
        Assert.Equal("bones", match.Studio);
        Assert.InRange(match.Similarity, .92, .94);
        Assert.Equal("08:52", TraceMoeMatch.Timestamp(match.From));
        Assert.Equal("api.trace.moe", match.Video!.Host);
    }

    [Fact]
    public void Handles_unsorted_matches_title_fallback_and_string_episode_without_trusting_urls()
    {
        var match = TraceMoeProvider.Parse("""
            {"result":[
             {"anilist":{"id":1,"title":{"english":"Low"}},"similarity":0.1,"from":0,"to":1},
             {"anilist":{"id":2,"title":{"romaji":"Best"},"duration":24},"episode":"395|615",
              "similarity":0.95,"from":62,"to":65,"image":"https://evil.test/image","video":"file:///C:/secret"}]}
            """);
        Assert.Equal("Best", match!.Title);
        Assert.Equal("395|615", match.Episode);
        Assert.Equal(1440, match.Duration);
        Assert.Equal("https://anilist.co/anime/2", match.AnilistUrl);
        Assert.Null(match.Video);
        Assert.Null(match.Image);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"error\":\"busy\",\"result\":[]}")]
    [InlineData("{\"result\":[{}]}")]
    public async Task Malformed_response_is_reported(string json)
    {
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) })));
        var outcome = await new TraceMoeProvider(client).PrepareAsync([1], CancellationToken.None);
        Assert.Equal(UploadFailure.BadResponse, outcome.Failure);
    }

    [Fact]
    public async Task Posts_jpeg_binary_and_preserves_empty_result_without_a_browser()
    {
        using var client = new HttpClient(new Handler(async (request, cancel) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.trace.moe/search?anilistInfo", request.RequestUri!.AbsoluteUri);
            Assert.Equal("image/jpeg", request.Content!.Headers.ContentType!.MediaType);
            Assert.Equal(new byte[] { 1, 2, 3 }, await request.Content.ReadAsByteArrayAsync(cancel));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"result\":[]}") };
        }));
        var outcome = await new TraceMoeProvider(client).PrepareAsync([1, 2, 3], CancellationToken.None);
        Assert.True(outcome.Success);
        Assert.Equal(PreparedVisualSearchKind.TraceMoe, outcome.PreparedSearch!.Kind);
        Assert.Null(outcome.PreparedSearch.TraceMatch);
        Assert.Null(outcome.PreparedSearch.BrowserOperation);
    }

    [Fact]
    public async Task Cancellation_is_distinguished_from_timeout_and_network_failure()
    {
        using var client = new HttpClient(new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage();
        }));
        using var cancellation = new CancellationTokenSource();
        var task = new TraceMoeProvider(client).PrepareAsync([1], cancellation.Token);
        cancellation.Cancel();
        Assert.Equal(UploadFailure.Canceled, (await task).Failure);
    }

    [Fact]
    public async Task Rate_limit_keeps_status_code()
    {
        using var client = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests))));
        var outcome = await new TraceMoeProvider(client).PrepareAsync([1], CancellationToken.None);
        Assert.Equal(429, outcome.StatusCode);
        Assert.Equal(UploadFailure.UnexpectedStatus, outcome.Failure);
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
