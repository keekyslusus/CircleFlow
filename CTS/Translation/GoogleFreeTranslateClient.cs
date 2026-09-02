namespace CircleToSearch.Translation;

using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using CircleToSearch.Ocr;
using GdiBitmap = System.Drawing.Bitmap;

public sealed class GoogleFreeTranslateClient : ITranslationService
{
    private readonly HttpClient _httpClient;
    private readonly PluginLog? _log;

    public GoogleFreeTranslateClient(HttpClient httpClient, PluginLog? log = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _log = log;
    }

    public async Task<string> TranslateTextAsync(
        string text,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var effectiveTarget = ResolveTargetLanguage(targetLanguage);

        var url = FormattableString.Invariant(
            $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={Uri.EscapeDataString(effectiveTarget)}&dt=t&q={Uri.EscapeDataString(text)}");

        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ParseTranslationJson(json);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log?.Error(nameof(GoogleFreeTranslateClient), $"Translation request failed for target '{effectiveTarget}'", ex);
            throw;
        }
    }

    public async Task<IReadOnlyList<TranslationBlock>> TranslateScreenAsync(
        IReadOnlyList<OcrLineSnapshot> lines,
        GdiBitmap frame,
        double scale,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        if (lines.Count == 0) return [];
        var effectiveTarget = ResolveTargetLanguage(targetLanguage);

        var groups = GroupLinesIntoBlocks(lines);
        var blocks = new List<TranslationBlock>(groups.Count);

        foreach (var group in groups)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var combinedText = string.Join("\n", group.Select(l => l.Text));
            if (string.IsNullOrWhiteSpace(combinedText)) continue;

            var unionDipRect = group.Select(l => l.DipRect).Aggregate(Rect.Union);
            var avgHeight = group.Average(l => l.DipRect.Height);
            var fontSize = Math.Clamp(Math.Round(avgHeight * 0.72), 11, 40);

            var bgColor = DominantColorSampler.SampleBackgroundColor(frame, unionDipRect, scale);
            var textColor = DominantColorSampler.GetContrastingTextColor(bgColor);

            string translated;
            try
            {
                translated = await TranslateTextAsync(combinedText, effectiveTarget, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log?.Warn(nameof(GoogleFreeTranslateClient), $"translating block failed: {ex.Message}");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(translated))
            {
                blocks.Add(new TranslationBlock(
                    unionDipRect,
                    combinedText,
                    translated,
                    fontSize,
                    bgColor,
                    textColor,
                    FontWeights.Normal));
            }
        }

        return blocks;
    }

    internal static List<List<OcrLineSnapshot>> GroupLinesIntoBlocks(IReadOnlyList<OcrLineSnapshot> lines)
    {
        var result = new List<List<OcrLineSnapshot>>();
        if (lines.Count == 0) return result;

        var currentGroup = new List<OcrLineSnapshot> { lines[0] };
        for (var i = 1; i < lines.Count; i++)
        {
            var prev = lines[i - 1];
            var curr = lines[i];

            var verticalGap = curr.DipRect.Top - prev.DipRect.Bottom;
            var avgHeight = (curr.DipRect.Height + prev.DipRect.Height) / 2;

            var horizontalOverlap = Math.Max(0,
                Math.Min(prev.DipRect.Right, curr.DipRect.Right) - Math.Max(prev.DipRect.Left, curr.DipRect.Left));

            if (verticalGap >= -avgHeight * 0.5 && verticalGap <= avgHeight * 1.6 && horizontalOverlap > 0)
            {
                currentGroup.Add(curr);
            }
            else
            {
                result.Add(currentGroup);
                currentGroup = [curr];
            }
        }
        result.Add(currentGroup);
        return result;
    }

    internal static string ParseTranslationJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            return string.Empty;

        var sentences = root[0];
        if (sentences.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var sentence in sentences.EnumerateArray())
        {
            if (sentence.ValueKind == JsonValueKind.Array && sentence.GetArrayLength() > 0)
            {
                var text = sentence[0].GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    sb.Append(text);
                }
            }
        }
        return sb.ToString();
    }

    private static string ResolveTargetLanguage(string requested)
    {
        if (!string.IsNullOrWhiteSpace(requested)) return requested;
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return string.IsNullOrWhiteSpace(culture) ? "en" : culture;
    }
}
