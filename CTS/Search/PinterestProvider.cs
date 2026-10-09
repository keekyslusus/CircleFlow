using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CircleToSearch.Search;

public sealed record PinterestPin(string Id, string Title, string Domain, Uri? Link, Uri Image, Uri FullImage, int Width, int Height)
{
    public string PinUrl => $"https://www.pinterest.com/pin/{Id}/";
}

public sealed class PinterestProvider(HttpClient client) : IVisualSearchProvider
{
    // The same endpoint Pinterest's own browser extension uses; it needs no account or key.
    private const string Endpoint = "https://api.pinterest.com/v3/visual_search/extension/image/";

    public async Task<VisualSearchPreparationOutcome> PrepareAsync(byte[] jpeg, CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            using var image = new ByteArrayContent(jpeg);
            image.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            using var content = new MultipartFormDataContent
            {
                { image, "image", "selection.jpg" },
                { new StringContent("0"), "x" },
                { new StringContent("0"), "y" },
                { new StringContent("1"), "w" },
                { new StringContent("1"), "h" },
                { new StringContent("https"), "base_scheme" },
            };
            using var response = await client.PutAsync(Endpoint, content, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return VisualSearchPreparationOutcome.Fail(UploadFailure.UnexpectedStatus, (int)response.StatusCode);
            var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return VisualSearchPreparationOutcome.Ready(PreparedVisualSearch.ForPinterest(Parse(json)));
        }
        catch (OperationCanceledException)
        {
            return VisualSearchPreparationOutcome.Fail(cancel.IsCancellationRequested ? UploadFailure.Canceled : UploadFailure.Timeout);
        }
        catch (HttpRequestException) { return VisualSearchPreparationOutcome.Fail(UploadFailure.NetworkError); }
        catch (JsonException) { return VisualSearchPreparationOutcome.Fail(UploadFailure.BadResponse); }
    }

    internal static IReadOnlyList<PinterestPin> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || Text(root, "status") != "success" ||
            !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new JsonException("Invalid Pinterest response.");
        var pins = new List<PinterestPin>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || Text(item, "type") is not (null or "pin")) continue;
            var id = Text(item, "id");
            if (string.IsNullOrEmpty(id) || !id.All(char.IsAsciiDigit) || !seen.Add(id)) continue;
            // The 474 px medium image keeps previews light; copying and saving want the 1200 px one.
            var full = PinImage(Text(item, "image_large_url"));
            var image = PinImage(Text(item, "image_medium_url")) ?? full;
            if (image is null) continue;
            var title = Clean(Text(item, "title"));
            if (title.Length == 0) title = Clean(Text(item, "description"));
            var domain = Clean(Text(item, "domain"));
            var link = WebLink(Text(item, "link"));
            // Pins without an external source report a placeholder instead of a domain.
            if (link is null) domain = "";
            var (width, height) = Size(item, "image_medium_size_pixels");
            pins.Add(new(id, title, domain, link, image, full ?? image, width, height));
        }
        return pins;
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            }
            : null;

    private static string Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static (int Width, int Height) Size(JsonElement item, string property) =>
        item.TryGetProperty(property, out var size) && size.ValueKind == JsonValueKind.Object &&
        size.TryGetProperty("width", out var w) && w.ValueKind == JsonValueKind.Number && w.TryGetInt32(out var width) && width > 0 &&
        size.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number && h.TryGetInt32(out var height) && height > 0
            ? (width, height)
            : (0, 0);

    private static Uri? PinImage(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host == "i.pinimg.com" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) ? uri : null;

    private static Uri? WebLink(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) && string.IsNullOrEmpty(uri.UserInfo)
            ? uri
            : null;
}
