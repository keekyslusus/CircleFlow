using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CircleToSearch.Search;

public sealed record TraceMoeMatch(
    int AnilistId, string Title, string NativeTitle, string Episode, string Format,
    string Year, string Studio, double Similarity, double From, double To,
    double Duration, Uri? Image, Uri? Video)
{
    public string AnilistUrl => $"https://anilist.co/anime/{AnilistId}";
    public static string Timestamp(double seconds) => TimeSpan.FromSeconds(Math.Round(Math.Max(0, seconds)))
        .ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"mm\:ss", CultureInfo.InvariantCulture);
}

public sealed class TraceMoeProvider(HttpClient client) : IVisualSearchProvider
{
    public async Task<VisualSearchPreparationOutcome> PrepareAsync(byte[] png, CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            using var content = new ByteArrayContent(png);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            using var response = await client.PostAsync("https://api.trace.moe/search?anilistInfo", content, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return VisualSearchPreparationOutcome.Fail(UploadFailure.UnexpectedStatus, (int)response.StatusCode);
            var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return VisualSearchPreparationOutcome.Ready(PreparedVisualSearch.ForTraceMoe(Parse(json)));
        }
        catch (OperationCanceledException)
        {
            return VisualSearchPreparationOutcome.Fail(cancel.IsCancellationRequested ? UploadFailure.Canceled : UploadFailure.Timeout);
        }
        catch (HttpRequestException) { return VisualSearchPreparationOutcome.Fail(UploadFailure.NetworkError); }
        catch (JsonException) { return VisualSearchPreparationOutcome.Fail(UploadFailure.BadResponse); }
    }

    internal static TraceMoeMatch? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !string.IsNullOrEmpty(Text(root, "error")) ||
            !root.TryGetProperty("result", out var results) || results.ValueKind != JsonValueKind.Array)
            throw new JsonException("Invalid trace.moe response.");
        TraceMoeMatch? best = null;
        foreach (var item in results.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("anilist", out var anime) ||
                anime.ValueKind != JsonValueKind.Object || !anime.TryGetProperty("id", out var id) ||
                id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out var animeId) || animeId <= 0) continue;
            var similarity = Number(item, "similarity");
            if (similarity < 0 || similarity > 1 || (best is not null && best.Similarity >= similarity)) continue;
            if (!anime.TryGetProperty("title", out var titles) || titles.ValueKind != JsonValueKind.Object) continue;
            var title = Text(titles, "english") ?? Text(titles, "romaji") ?? Text(titles, "native");
            if (string.IsNullOrWhiteSpace(title)) continue;
            var from = Number(item, "from");
            var to = Number(item, "to");
            var duration = Number(item, "duration");
            if (from < 0 || to < from || to >= TimeSpan.MaxValue.TotalSeconds || duration >= TimeSpan.MaxValue.TotalSeconds) continue;
            if (duration <= 0) duration = Number(anime, "duration") * 60;
            duration = Math.Max(duration, to);
            if (!double.IsFinite(duration) || duration >= TimeSpan.MaxValue.TotalSeconds - 1) continue;
            string studio = "";
            if (anime.TryGetProperty("studios", out var studios) && studios.ValueKind == JsonValueKind.Object &&
                studios.TryGetProperty("edges", out var edges) && edges.ValueKind == JsonValueKind.Array)
                foreach (var edge in edges.EnumerateArray())
                    if (edge.ValueKind == JsonValueKind.Object && edge.TryGetProperty("isMain", out var main) && main.ValueKind == JsonValueKind.True &&
                        edge.TryGetProperty("node", out var node)) { studio = Text(node, "name") ?? ""; break; }
            best = new(animeId, title, Text(titles, "native") ?? "", Text(item, "episode") ?? "",
                Text(anime, "format") ?? "", Text(anime, "seasonYear") ?? "", studio,
                similarity, from, to, duration, MediaUri(Text(item, "image")), MediaUri(Text(item, "video")));
        }
        if (results.GetArrayLength() > 0 && best is null) throw new JsonException("No valid trace.moe results.");
        return best;
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value)
            ? value.ValueKind switch { JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.GetRawText(), _ => null }
            : null;

    private static double Number(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out var number) && double.IsFinite(number) ? number : -1;

    private static Uri? MediaUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.Host == "api.trace.moe" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) ? uri : null;
}
