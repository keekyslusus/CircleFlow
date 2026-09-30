using System.Net.Http;
using System.Text.Json;

namespace CircleToSearch.Search;

// Yandex reverse image search: the raw image is POSTed to the images-apphost upload endpoint and
// the JSON answer carries a cbir_id whose avatars.mds.yandex.net URL feeds the results page.
// Anonymous, no API key, no cookies; JPEG is accepted.
public sealed class YandexImagesProvider : IVisualSearchProvider, IDisposable
{
    public const string UploadUrl = "https://yandex.com/images-apphost/image-download";
    public const string UploadQuery = "cbird=111&images_avatars_size=preview&images_avatars_namespace=images-cbir";

    private readonly HttpClient _client;
    private readonly TimeSpan _timeout;
    private readonly PluginLog? _log;
    private int _disposed;

    public YandexImagesProvider()
        : this(new SocketsHttpHandler { AllowAutoRedirect = false })
    {
    }

    public YandexImagesProvider(PluginLog? log)
        : this(new SocketsHttpHandler { AllowAutoRedirect = false }, log: log)
    {
    }

    public YandexImagesProvider(HttpMessageHandler handler, TimeSpan? timeout = null, PluginLog? log = null)
    {
        // HttpClient.Timeout stops at the response headers under ResponseHeadersRead, so the deadline
        // is enforced by a token that also covers reading the body.
        _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
        _log = log;
    }

    public async Task<VisualSearchPreparationOutcome> PrepareAsync(byte[] jpeg, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{UploadUrl}?{UploadQuery}")
        {
            Content = new ByteArrayContent(jpeg),
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        deadline.CancelAfter(_timeout);

        try
        {
            using var response = await _client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return VisualSearchPreparationOutcome.Fail(UploadFailure.UnexpectedStatus, (int)response.StatusCode);

            var body = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);
            var parsed = Parse(body);
            if (parsed is null)
            {
                _log?.Warn(
                    nameof(YandexImagesProvider),
                    $"unexpected upload response with {body.Length} characters");
                return VisualSearchPreparationOutcome.Fail(UploadFailure.BadResponse, (int)response.StatusCode);
            }

            var (cbirId, imagePath) = parsed.Value;
            var resultsUrl = $"https://yandex.com/images/search?rpt=imageview" +
                             $"&url={Uri.EscapeDataString(imagePath)}" +
                             $"&cbir_id={Uri.EscapeDataString(cbirId)}";
            if (!YandexResultUrlPolicy.IsAllowed(new Uri(resultsUrl)))
                return VisualSearchPreparationOutcome.Fail(UploadFailure.PolicyRejection, (int)response.StatusCode);

            var verifiedUrl = new Uri(resultsUrl);
            return VisualSearchPreparationOutcome.Ready(
                PreparedVisualSearch.ForUrl(verifiedUrl, verifiedUrl));
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return VisualSearchPreparationOutcome.Fail(UploadFailure.Canceled);
        }
        catch (OperationCanceledException)
        {
            return VisualSearchPreparationOutcome.Fail(UploadFailure.Timeout);
        }
        catch (HttpRequestException)
        {
            return VisualSearchPreparationOutcome.Fail(UploadFailure.NetworkError);
        }
    }

    private static (string CbirId, string ImagePath)? Parse(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("cbir_id", out var cbirIdElement)
                || cbirIdElement.ValueKind != JsonValueKind.String) return null;

            var cbirId = cbirIdElement.GetString();
            if (string.IsNullOrWhiteSpace(cbirId)) return null;

            var imagePath = "https://avatars.mds.yandex.net/get-images-cbir/" + cbirId + "/orig";
            if (root.TryGetProperty("sizes", out var sizes)
                && sizes.ValueKind == JsonValueKind.Object
                && sizes.TryGetProperty("orig", out var orig)
                && orig.ValueKind == JsonValueKind.Object
                && orig.TryGetProperty("path", out var pathElement)
                && pathElement.ValueKind == JsonValueKind.String)
            {
                var path = pathElement.GetString();
                if (!string.IsNullOrWhiteSpace(path)) imagePath = path;
            }
            return (cbirId, imagePath);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _client.Dispose();
    }
}
