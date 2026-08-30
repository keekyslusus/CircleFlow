// SPDX-License-Identifier: GPL-3.0-or-later

using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using CircleToSearch.MusicRecognition.Fingerprinting;

namespace CircleToSearch.MusicRecognition.Shazam;

public interface IShazamClient
{
    Task<ShazamRecognition?> RecognizeAsync(ShazamSignature signature, CancellationToken cancellationToken);
}

public sealed class ShazamRateLimitException : HttpRequestException
{
    public ShazamRateLimitException(TimeSpan? retryAfter)
        : base("Shazam returned HTTP 429.", null, HttpStatusCode.TooManyRequests) => RetryAfter = retryAfter;

    public TimeSpan? RetryAfter { get; }
}

public sealed class ShazamClient : IShazamClient
{
    private readonly HttpClient _httpClient;
    private readonly string _userAgent;

    public ShazamClient(HttpClient httpClient, IShazamUserAgentProvider userAgentProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(userAgentProvider);
        _httpClient = httpClient;
        _userAgent = userAgentProvider.Select();
    }

    internal string UserAgent => _userAgent;

    public async Task<ShazamRecognition?> RecognizeAsync(
        ShazamSignature signature,
        CancellationToken cancellationToken)
    {
        var firstId = Guid.NewGuid().ToString().ToUpperInvariant();
        var secondId = Guid.NewGuid().ToString();
        var url = $"https://amp.shazam.com/discovery/v5/en/US/android/-/tag/{firstId}/{secondId}" +
                  "?sync=true&webv3=true&sampling=true&connected=&shazamapiversion=v3&sharehub=true&video=v3";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var payload = new
        {
            geolocation = new { altitude = 300, latitude = 45, longitude = 2 },
            signature = new
            {
                samplems = signature.DurationMilliseconds,
                timestamp,
                uri = ShazamSignatureCodec.EncodeToUri(signature),
            },
            timestamp,
            timezone = "Europe/Paris",
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = JsonContent.Create(payload),
        };
        request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
        request.Headers.TryAddWithoutValidation("Content-Language", "en_US");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new ShazamRateLimitException(ReadRetryAfter(response));
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Shazam returned HTTP {(int)response.StatusCode}.",
                null,
                response.StatusCode);

        await using var json = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(json, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("track", out var track)) return null;
        var title = GetString(track, "title");
        var artist = GetString(track, "subtitle");
        if (title is null || artist is null) return null;

        return new ShazamRecognition(
            title,
            artist,
            GetAlbum(track),
            GetNestedString(track, "genres", "primary"),
            GetString(track, "key"),
            GetNestedString(track, "images", "coverart"),
            GetNestedString(track, "share", "href"));
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var retry = response.Headers.RetryAfter;
        if (retry?.Delta is { } delta && delta > TimeSpan.Zero) return delta;
        if (retry?.Date is { } date)
        {
            var duration = date - DateTimeOffset.UtcNow;
            if (duration > TimeSpan.Zero) return duration;
        }
        return null;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? GetNestedString(JsonElement element, string parent, string name) =>
        element.TryGetProperty(parent, out var nested) ? GetString(nested, name) : null;

    private static string? GetAlbum(JsonElement track)
    {
        if (!track.TryGetProperty("sections", out var sections) || sections.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var section in sections.EnumerateArray())
        {
            if (GetString(section, "type") != "SONG" ||
                !section.TryGetProperty("metadata", out var metadata) ||
                metadata.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var item in metadata.EnumerateArray())
                if (GetString(item, "title") == "Album") return GetString(item, "text");
        }
        return null;
    }
}
