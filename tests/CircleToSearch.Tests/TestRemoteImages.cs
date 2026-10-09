using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CircleToSearch.Ui;

namespace CircleToSearch.Tests;

// Image loaders for UI tests that never reach the network.
internal static class TestRemoteImages
{
    // Serves file URIs from disk and answers everything else with 404.
    internal static RemoteImageLoader Offline { get; } = new(new HttpClient(Responding(request =>
        request.RequestUri is { IsFile: true } uri && File.Exists(uri.LocalPath) ? File.ReadAllBytes(uri.LocalPath) : null)));

    internal static RemoteImageLoader Serving(byte[] image) => new(new HttpClient(Responding(_ => image)));

    // A null body answers 404.
    internal static HttpMessageHandler Responding(Func<HttpRequestMessage, byte[]?> content) => new Handler(content);

    internal static byte[] Png(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = 200;
            pixels[offset + 1] = 90;
            pixels[offset + 2] = 40;
            pixels[offset + 3] = 255;
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private sealed class Handler(Func<HttpRequestMessage, byte[]?> content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(content(request) is { } bytes
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
