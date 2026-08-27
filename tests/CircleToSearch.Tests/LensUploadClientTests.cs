using System.Net;
using System.Net.Http;
using CircleToSearch.Search;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class LensUploadClientTests
{
    [Fact]
    public async Task Redirect_302_yields_the_results_url()
    {
        var handler = new FakeHandler
        {
            Responder = () =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new Uri("https://lens.google.com/v3/upload?udm=26&vsrid=abc");
                return response;
            },
        };
        var provider = new GoogleLensProvider(handler);

        var outcome = await provider.SearchAsync([1, 2, 3], CancellationToken.None);

        Assert.True(outcome.Success);
        Assert.Equal("https://lens.google.com/v3/upload?udm=26&vsrid=abc", outcome.ResultsUrl);
    }

    [Fact]
    public async Task Consent_page_200_maps_to_unexpected_status()
    {
        var outcome = await SearchWith(() => new HttpResponseMessage(HttpStatusCode.OK));

        Assert.False(outcome.Success);
        Assert.Equal(LensUploadFailure.UnexpectedStatus, outcome.Failure);
        Assert.Equal(200, outcome.StatusCode);
    }

    [Fact]
    public async Task Rate_limit_429_maps_to_unexpected_status()
    {
        var outcome = await SearchWith(() => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        Assert.False(outcome.Success);
        Assert.Equal(LensUploadFailure.UnexpectedStatus, outcome.Failure);
        Assert.Equal(429, outcome.StatusCode);
    }

    [Fact]
    public async Task Redirect_without_location_maps_to_empty_location()
    {
        var outcome = await SearchWith(() => new HttpResponseMessage(HttpStatusCode.Found));

        Assert.Equal(LensUploadFailure.EmptyLocation, outcome.Failure);
    }

    [Fact]
    public async Task Hostile_location_is_rejected_by_policy()
    {
        var outcome = await SearchWith(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("https://google.com.evil.test/results");
            return response;
        });

        Assert.Equal(LensUploadFailure.PolicyRejection, outcome.Failure);
    }

    [Fact]
    public async Task Network_exception_maps_to_network_error()
    {
        var handler = new FakeHandler
        {
            Responder = () => throw new HttpRequestException("boom"),
        };

        var outcome = await new GoogleLensProvider(handler).SearchAsync([1], CancellationToken.None);

        Assert.Equal(LensUploadFailure.NetworkError, outcome.Failure);
    }

    [Fact]
    public async Task Provider_timeout_maps_to_timeout_failure()
    {
        var handler = new FakeHandler
        {
            Delay = token => Task.Delay(Timeout.InfiniteTimeSpan, token),
        };

        var outcome = await new GoogleLensProvider(handler, TimeSpan.FromMilliseconds(50))
            .SearchAsync([1], CancellationToken.None);

        Assert.Equal(LensUploadFailure.Timeout, outcome.Failure);
    }

    [Fact]
    public async Task Request_is_multipart_with_the_image_field()
    {
        var handler = new FakeHandler();
        var provider = new GoogleLensProvider(handler);

        await provider.SearchAsync([1, 2, 3], CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(GoogleLensProvider.UploadUrl, handler.LastRequest.RequestUri!.ToString());
        Assert.Contains("name=encoded_image", handler.LastBody);
        Assert.Contains("filename=capture.png", handler.LastBody);
        Assert.Contains("image/png", handler.LastBody);
    }

    [Fact]
    public async Task External_cancellation_maps_to_canceled()
    {
        var handler = new FakeHandler
        {
            Delay = token => Task.Delay(Timeout.InfiniteTimeSpan, token),
        };
        using var cancel = new CancellationTokenSource(50);

        var outcome = await new GoogleLensProvider(handler, TimeSpan.FromSeconds(30))
            .SearchAsync([1], cancel.Token);

        Assert.Equal(LensUploadFailure.Canceled, outcome.Failure);
    }

    private static async Task<VisualSearchOutcome> SearchWith(Func<HttpResponseMessage> responder)
    {
        var provider = new GoogleLensProvider(new FakeHandler { Responder = responder });
        return await provider.SearchAsync([1, 2, 3], CancellationToken.None);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        public Func<HttpResponseMessage> Responder { get; init; } = () => new HttpResponseMessage(HttpStatusCode.OK);

        public Func<CancellationToken, Task>? Delay { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            if (Delay is not null)
                await Delay(cancellationToken);
            return Responder();
        }
    }
}
