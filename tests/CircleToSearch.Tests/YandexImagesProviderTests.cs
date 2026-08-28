using System.Net;
using System.Net.Http;
using CircleToSearch.Search;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class YandexImagesProviderTests
{
    private const string UploadResponseJson =
        """
        {
          "cbir_id":"1382825/Li2HdR6qGtqUzyErZ9utIw7363",
          "sizes":{"orig":{"path":"https://avatars.mds.yandex.net/get-images-cbir/1382825/Li2HdR6qGtqUzyErZ9utIw7363/orig"}}
        }
        """;

    [Fact]
    public async Task Successful_upload_builds_the_results_url()
    {
        var provider = new YandexImagesProvider(JsonHandler(UploadResponseJson));

        var outcome = await provider.SearchAsync([1, 2, 3], CancellationToken.None);

        Assert.True(outcome.Success);
        Assert.Equal(
            "https://yandex.ru/images/search?rpt=imageview" +
            "&url=https%3A%2F%2Favatars.mds.yandex.net%2Fget-images-cbir%2F1382825%2FLi2HdR6qGtqUzyErZ9utIw7363%2Forig" +
            "&cbir_id=1382825%2FLi2HdR6qGtqUzyErZ9utIw7363",
            outcome.ResultsUrl);
    }

    [Fact]
    public async Task Upload_sends_raw_image_bytes_to_the_apphost_endpoint()
    {
        var handler = JsonHandler(UploadResponseJson);
        var provider = new YandexImagesProvider(handler);

        await provider.SearchAsync([1, 2, 3], CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(new Uri(YandexImagesProvider.UploadUrl + "?" + YandexImagesProvider.UploadQuery),
            handler.LastRequest.RequestUri);
        Assert.Equal("image/png", handler.LastRequest.Content!.Headers.ContentType!.MediaType);
        Assert.Equal([1, 2, 3], handler.LastBody);
    }

    [Fact]
    public async Task Missing_cbir_id_maps_to_bad_response()
    {
        var provider = new YandexImagesProvider(JsonHandler("""{"error":"nope"}"""));

        var outcome = await provider.SearchAsync([1], CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.Equal(UploadFailure.BadResponse, outcome.Failure);
    }

    [Fact]
    public async Task Broken_json_maps_to_bad_response()
    {
        var provider = new YandexImagesProvider(JsonHandler("not json at all"));

        var outcome = await provider.SearchAsync([1], CancellationToken.None);

        Assert.Equal(UploadFailure.BadResponse, outcome.Failure);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Error_status_maps_to_unexpected_status(HttpStatusCode status)
    {
        var provider = new YandexImagesProvider(JsonHandler("", status));

        var outcome = await provider.SearchAsync([1], CancellationToken.None);

        Assert.False(outcome.Success);
        Assert.Equal(UploadFailure.UnexpectedStatus, outcome.Failure);
        Assert.Equal((int)status, outcome.StatusCode);
    }

    [Fact]
    public async Task Network_exception_maps_to_network_error()
    {
        var handler = new ThrowingHandler(new HttpRequestException("boom"));
        var provider = new YandexImagesProvider(handler);

        var outcome = await provider.SearchAsync([1], CancellationToken.None);

        Assert.Equal(UploadFailure.NetworkError, outcome.Failure);
    }

    [Fact]
    public async Task External_cancellation_maps_to_canceled()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var provider = new YandexImagesProvider(new ThrowingHandler(new OperationCanceledException(cancel.Token)), TimeSpan.FromSeconds(30));

        var outcome = await provider.SearchAsync([1], cancel.Token);

        Assert.Equal(UploadFailure.Canceled, outcome.Failure);
    }

    [Fact]
    public void Dispose_releases_the_http_handler_once()
    {
        var handler = new TrackingHandler();
        var provider = new YandexImagesProvider(handler);

        provider.Dispose();
        provider.Dispose();

        Assert.Equal(1, handler.DisposeCalls);
    }

    private static FakeHandler JsonHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(() =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
            return response;
        });

    private sealed class FakeHandler(Func<HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public byte[]? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            return responder();
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class TrackingHandler : HttpMessageHandler
    {
        public int DisposeCalls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) DisposeCalls++;
            base.Dispose(disposing);
        }
    }
}
