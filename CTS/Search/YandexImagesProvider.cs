using System.Net.Http;
using System.Text.Json;

namespace CircleToSearch.Search;

// Yandex reverse image search: the raw image is POSTed to the images-apphost upload endpoint and
// the JSON answer carries a cbir_id whose avatars.mds.yandex.net URL feeds the results page.
// Anonymous, no API key, no cookies; PNG is accepted as-is (verified 2026-08-28).
public sealed class YandexImagesProvider : IVisualSearchProvider, IDisposable
{
    public const string UploadUrl = "https://yandex.ru/images-apphost/image-download";
    public const string UploadQuery = "cbird=111&images_avatars_size=preview&images_avatars_namespace=images-cbir";

    private readonly HttpClient _client;
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
        _client = new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromSeconds(10) };
        _log = log;
    }

    public async Task<VisualSearchOutcome> SearchAsync(byte[] png, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{UploadUrl}?{UploadQuery}")
        {
            Content = new ByteArrayContent(png),
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");

        try
        {
            using var response = await _client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return VisualSearchOutcome.Fail(UploadFailure.UnexpectedStatus, (int)response.StatusCode);

            var body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);
            var parsed = Parse(body);
            if (parsed is null)
            {
                _log?.Warn(nameof(YandexImagesProvider), $"unexpected upload response: {body[..Math.Min(200, body.Length)]}");
                return VisualSearchOutcome.Fail(UploadFailure.BadResponse, (int)response.StatusCode);
            }

            var (cbirId, imagePath) = parsed.Value;
            var resultsUrl = $"https://yandex.ru/images/search?rpt=imageview" +
                             $"&url={Uri.EscapeDataString(imagePath)}" +
                             $"&cbir_id={Uri.EscapeDataString(cbirId)}";
            if (!YandexResultUrlPolicy.IsAllowed(new Uri(resultsUrl)))
                return VisualSearchOutcome.Fail(UploadFailure.PolicyRejection, (int)response.StatusCode);

            return VisualSearchOutcome.Ok(resultsUrl);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return VisualSearchOutcome.Fail(UploadFailure.Canceled);
        }
        catch (OperationCanceledException)
        {
            return VisualSearchOutcome.Fail(UploadFailure.Timeout);
        }
        catch (HttpRequestException)
        {
            return VisualSearchOutcome.Fail(UploadFailure.NetworkError);
        }
    }

    private static (string CbirId, string ImagePath)? Parse(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (!root.TryGetProperty("cbir_id", out var cbirIdElement)) return null;

            var cbirId = cbirIdElement.GetString();
            if (string.IsNullOrWhiteSpace(cbirId)) return null;

            var imagePath = "https://avatars.mds.yandex.net/get-images-cbir/" + cbirId + "/orig";
            if (root.TryGetProperty("sizes", out var sizes)
                && sizes.TryGetProperty("orig", out var orig)
                && orig.TryGetProperty("path", out var pathElement))
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
