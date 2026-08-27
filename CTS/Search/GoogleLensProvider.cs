using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace CircleToSearch.Search;

public sealed class GoogleLensProvider : IVisualSearchProvider
{
    public const string UploadUrl = "https://www.google.com/searchbyimage/upload";

    private readonly HttpClient _client;

    public GoogleLensProvider()
        : this(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
    }

    public GoogleLensProvider(HttpMessageHandler handler, TimeSpan? timeout = null)
    {
        _client = new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromSeconds(10) };
    }

    public async Task<VisualSearchOutcome> SearchAsync(byte[] png, CancellationToken cancel)
    {
        using var content = new MultipartFormDataContent();
        var image = new ByteArrayContent(png);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(image, "encoded_image", "capture.png");

        using var request = new HttpRequestMessage(HttpMethod.Post, UploadUrl) { Content = content };
        try
        {
            using var response = await _client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel)
                .ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.Found)
                return VisualSearchOutcome.Fail(LensUploadFailure.UnexpectedStatus, (int)response.StatusCode);

            var location = response.Headers.Location;
            if (location is null)
                return VisualSearchOutcome.Fail(LensUploadFailure.EmptyLocation, 302);
            if (!RedirectUrlPolicy.IsAllowed(location))
                return VisualSearchOutcome.Fail(LensUploadFailure.PolicyRejection, 302);

            return VisualSearchOutcome.Ok(location.AbsoluteUri);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return VisualSearchOutcome.Fail(LensUploadFailure.Canceled);
        }
        catch (OperationCanceledException)
        {
            return VisualSearchOutcome.Fail(LensUploadFailure.Timeout);
        }
        catch (HttpRequestException)
        {
            return VisualSearchOutcome.Fail(LensUploadFailure.NetworkError);
        }
    }
}
