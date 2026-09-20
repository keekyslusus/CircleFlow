using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace CircleToSearch.Translation;

internal sealed record GoogleImageChallenge(string Program, string Interpreter, string? State);
internal sealed record ImageTranslationSignature(string Header, string UserAgent);

internal static class GoogleImageTranslationProtocol
{
    internal const string Origin = "https://translate.google.com";
    internal const string FrameUrl = Origin + "/_/TranslateWebserverUi/bscframe";
    internal const string Endpoint = Origin + "/_/TranslateWebserverUi/data/batchexecute?rpcids=WqWDPb&rt=c";
    internal const int MaxResponseBytes = 48 * 1024 * 1024;

    internal static string CreateRequest(byte[] png, string target) => JsonSerializer.Serialize(new object[]
    {
        new object[] { new object?[] { "WqWDPb", JsonSerializer.Serialize(new object[]
        { new[] { Convert.ToBase64String(png), "image/png" }, "auto", target }), null, "generic" } }
    });

    internal static GoogleImageChallenge ReadChallenge(string html)
    {
        var match = Regex.Match(html, "\"mnsUbf\"\\s*:\\s*(\"(?:\\\\.|[^\"\\\\])*\")",
            RegexOptions.None, TimeSpan.FromSeconds(1));
        if (!match.Success) throw new InvalidDataException("Image translation initialization is unavailable.");
        var encoded = JsonSerializer.Deserialize<string>(match.Groups[1].Value)!;
        if (!encoded.StartsWith("%.@.", StringComparison.Ordinal)) throw new InvalidDataException("Unexpected initialization encoding.");
        using var json = JsonDocument.Parse("[" + encoded[4..]);
        var fields = json.RootElement;
        return new GoogleImageChallenge(fields[0].GetString()!, fields[5][5].GetString()!, fields[7].GetString());
    }

    internal static BitmapSource ReadResponse(string body)
    {
        foreach (var line in body.Split('\n'))
        {
            if (!line.StartsWith("[[", StringComparison.Ordinal)) continue;
            using var records = JsonDocument.Parse(line);
            foreach (var record in records.RootElement.EnumerateArray())
            {
                if (record.ValueKind != JsonValueKind.Array || record.GetArrayLength() < 3 ||
                    record[0].GetString() != "wrb.fr" || record[1].GetString() != "WqWDPb") continue;
                using var payload = JsonDocument.Parse(record[2].GetString()!);
                var data = payload.RootElement;
                // Mixed-language images can omit the aggregate source language entirely.
                if (data.GetArrayLength() < 3 || data[1].ValueKind != JsonValueKind.String ||
                    data[2].ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(data[1].GetString()) ||
                    string.IsNullOrWhiteSpace(data[2].GetString()))
                    throw new InvalidDataException("No confirmed image translation was returned.");
                _ = data.GetArrayLength() > 3 ? data[3].GetString() : null;
                var mime = data[0][1].GetString();
                if (mime is not ("image/png" or "image/jpeg")) throw new InvalidDataException("Unexpected image type.");
                var bytes = Convert.FromBase64String(data[0][0].GetString()!);
                using var stream = new MemoryStream(bytes, writable: false);
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnDemand);
                var frame = decoder.Frames[0];
                if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 || (long)frame.PixelWidth * frame.PixelHeight > 40_000_000)
                    throw new InvalidDataException("Image dimensions exceed the translation limit.");
                // A frozen decoder frame still exposes thread-affine metadata to encoders; detach the pixels.
                var converted = new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                var stride = checked(frame.PixelWidth * 4);
                var pixels = new byte[checked(stride * frame.PixelHeight)];
                converted.CopyPixels(pixels, stride, 0);
                var image = BitmapSource.Create(frame.PixelWidth, frame.PixelHeight, 96, 96,
                    System.Windows.Media.PixelFormats.Bgra32, null, pixels, stride);
                image.Freeze();
                return image;
            }
        }
        throw new InvalidDataException("Translation RPC is absent.");
    }

    internal static async Task<string> ReadBoundedAsync(System.Net.Http.HttpContent content, int limit, CancellationToken cancellation)
    {
        if (content.Headers.ContentLength > limit) throw new InvalidDataException("Response exceeds the translation limit.");
        await using var input = await content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[32768];
        int count;
        while ((count = await input.ReadAsync(buffer, cancellation).ConfigureAwait(false)) != 0)
        {
            if (output.Length + count > limit) throw new InvalidDataException("Response exceeds the translation limit.");
            output.Write(buffer, 0, count);
        }
        return System.Text.Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);
    }
}
