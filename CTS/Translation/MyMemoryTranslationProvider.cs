using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.IO;
using System.Text;

namespace CircleToSearch.Translation;

public sealed class MyMemoryTranslationProvider : ITranslationProvider
{
    private const int MaximumResponseBytes = 64 * 1024;
    private static readonly Uri Endpoint = new("https://api.mymemory.translated.net/get");
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _timeout;

    public MyMemoryTranslationProvider(HttpClient httpClient, TimeSpan? timeout = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
        if (_timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public async Task<TranslationBatchOutcome> TranslateAsync(
        IReadOnlyList<TranslationChunk> chunks,
        string sourceLanguageTag,
        string targetLanguageTag,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        if (chunks.Any(chunk => Encoding.UTF8.GetByteCount(chunk.Text) > 500))
            return new TranslationBatchOutcome(chunks.Select(chunk =>
                new TranslatedChunk(chunk.LineId, chunk.Order, chunk.Text, null, TranslationFailure.BadResponse)).ToArray());
        var source = TranslationLanguageTags.ToProviderTag(sourceLanguageTag);
        var target = TranslationLanguageTags.ToProviderTag(targetLanguageTag);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        using var concurrency = new SemaphoreSlim(2, 2);
        var unique = chunks.Select(chunk => chunk.Text).Distinct(StringComparer.Ordinal).ToArray();
        var tasks = unique.ToDictionary(
            text => text,
            text => TranslateLimitedAsync(text, source, target, concurrency, timeout.Token, cancellationToken),
            StringComparer.Ordinal);
        await Task.WhenAll(tasks.Values).ConfigureAwait(false);
        var translated = chunks.Select(chunk =>
        {
            var result = tasks[chunk.Text].Result;
            return new TranslatedChunk(chunk.LineId, chunk.Order, chunk.Text, result.Text, result.Failure);
        }).ToArray();
        return new TranslationBatchOutcome(translated);
    }

    private async Task<ChunkResult> TranslateLimitedAsync(
        string text,
        string source,
        string target,
        SemaphoreSlim concurrency,
        CancellationToken timeoutToken,
        CancellationToken callerToken)
    {
        try
        {
            await concurrency.WaitAsync(timeoutToken).ConfigureAwait(false);
            try
            {
                var result = await TranslateOneAsync(text, source, target, timeoutToken).ConfigureAwait(false);
                if (result.Failure is not (TranslationFailure.Service or TranslationFailure.BadResponse)) return result;
                var fallbackSource = TranslationLanguageTags.ToNeutralProviderTag(source);
                var fallbackTarget = TranslationLanguageTags.ToNeutralProviderTag(target);
                if (fallbackSource == source && fallbackTarget == target) return result;
                return await TranslateOneAsync(text, fallbackSource, fallbackTarget, timeoutToken).ConfigureAwait(false);
            }
            finally { concurrency.Release(); }
        }
        catch (OperationCanceledException)
        {
            return new ChunkResult(null, callerToken.IsCancellationRequested
                ? TranslationFailure.Canceled
                : TranslationFailure.Timeout);
        }
        catch (HttpRequestException)
        {
            return new ChunkResult(null, TranslationFailure.Network);
        }
        catch (InvalidDataException)
        {
            return new ChunkResult(null, TranslationFailure.BadResponse);
        }
        catch
        {
            return new ChunkResult(null, TranslationFailure.Service);
        }
    }

    private async Task<ChunkResult> TranslateOneAsync(
        string text,
        string source,
        string target,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(text, source, target));
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            return new ChunkResult(null, TranslationFailure.RateLimited);
        if (!response.IsSuccessStatusCode)
            return new ChunkResult(null, TranslationFailure.Service);
        if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            return new ChunkResult(null, TranslationFailure.BadResponse);

        var bytes = await ReadBoundedAsync(response.Content, MaximumResponseBytes, cancellationToken).ConfigureAwait(false);
        try
        {
            using var json = JsonDocument.Parse(bytes);
            var root = json.RootElement;
            if (root.TryGetProperty("responseStatus", out var status) && !IsSuccessStatus(status))
                return new ChunkResult(null, status.ToString() == "429"
                    ? TranslationFailure.RateLimited
                    : TranslationFailure.Service);
            if (!root.TryGetProperty("responseData", out var data) ||
                !data.TryGetProperty("translatedText", out var translated) ||
                translated.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(translated.GetString()))
                return new ChunkResult(null, TranslationFailure.BadResponse);
            return new ChunkResult(WebUtility.HtmlDecode(translated.GetString()), TranslationFailure.None);
        }
        catch (JsonException)
        {
            return new ChunkResult(null, TranslationFailure.BadResponse);
        }
    }

    internal static Uri BuildUri(string text, string source, string target)
    {
        var builder = new UriBuilder(Endpoint)
        {
            Query = $"q={Uri.EscapeDataString(text)}&langpair={Uri.EscapeDataString(source + "|" + target)}",
        };
        var uri = builder.Uri;
        if (uri.Scheme != Uri.UriSchemeHttps || !string.Equals(uri.Host, Endpoint.Host, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Translation endpoint policy rejected the URI.");
        return uri;
    }

    private static bool IsSuccessStatus(JsonElement status) => status.ValueKind switch
    {
        JsonValueKind.Number => status.TryGetInt32(out var numeric) && numeric is >= 200 and < 300,
        JsonValueKind.String => int.TryParse(status.GetString(), out var numeric) && numeric is >= 200 and < 300,
        _ => true,
    };

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (memory.Length + read > maximumBytes) throw new InvalidDataException("Translation response exceeded the size limit.");
            memory.Write(buffer, 0, read);
        }
        return memory.ToArray();
    }

    private sealed record ChunkResult(string? Text, TranslationFailure Failure);
}
