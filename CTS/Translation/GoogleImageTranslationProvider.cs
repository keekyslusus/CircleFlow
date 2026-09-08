using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CircleToSearch.Translation;

internal interface IImageTranslationProvider
{
    Task<ImageTranslationData> TranslateAsync(BitmapSource source, string target, CancellationToken cancellation);
}

internal interface IImageTranslationSigner
{
    Task<ImageTranslationSignature> SignAsync(string request, string target, CancellationToken cancellation);
}

internal sealed class GoogleImageTranslationProvider(HttpClient http, IImageTranslationSigner signer) : IImageTranslationProvider
{
    public async Task<ImageTranslationData> TranslateAsync(BitmapSource source, string target, CancellationToken cancellation)
    {
        var png = await Task.Run(() => Encode(source), cancellation).ConfigureAwait(false);
        var requestBody = GoogleImageTranslationProtocol.CreateRequest(png, target);
        var signature = await signer.SignAsync(requestBody, target, cancellation).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        using var request = new HttpRequestMessage(HttpMethod.Post, GoogleImageTranslationProtocol.Endpoint);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["f.req"] = requestBody });
        request.Headers.TryAddWithoutValidation("User-Agent", signature.UserAgent);
        request.Headers.TryAddWithoutValidation("X-Goog-BatchExecute-Bgr", signature.Header);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await GoogleImageTranslationProtocol.ReadBoundedAsync(response.Content,
            GoogleImageTranslationProtocol.MaxResponseBytes, cancellation).ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        return GoogleImageTranslationProtocol.ReadResponse(body);
    }

    private static byte[] Encode(BitmapSource source)
    {
        BitmapSource image = source;
        var scale = Math.Min(1d, 4096d / Math.Max(source.PixelWidth, source.PixelHeight));
        if (scale < 1) image = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        if (stream.Length > 24 * 1024 * 1024) throw new InvalidDataException("Screenshot exceeds the translation limit.");
        return stream.ToArray();
    }
}
