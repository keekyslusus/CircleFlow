using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CircleToSearch.Ui;

// BitmapImage.UriSource downloads through a COM object that WPF caches process-wide on the first thread that used it;
// overlay threads exit after each session, which breaks every later download. Images are fetched here instead.
internal sealed class RemoteImageLoader(HttpClient client, long maxBytes = RemoteImageLoader.DefaultMaxBytes)
{
    private const long DefaultMaxBytes = 32 * 1024 * 1024;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    // Some image CDNs reject requests without a User-Agent; WPF's own download always sent one.
    private static readonly ProductInfoHeaderValue UserAgent = new("CircleFlow",
        typeof(RemoteImageLoader).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");

    // Never throws: a missing image leaves the caller's placeholder in place.
    public async Task<BitmapSource?> LoadAsync(Uri source, int decodePixelWidth = 0,
        BitmapCreateOptions createOptions = BitmapCreateOptions.None)
    {
        // Decoding on a pool thread would give that thread its own Dispatcher and window, so it returns to the caller's.
        var dispatcher = Dispatcher.FromThread(Thread.CurrentThread);
        try
        {
            using var timeout = new CancellationTokenSource(Timeout);
            var bytes = await DownloadAsync(source, timeout.Token).ConfigureAwait(false);
            return dispatcher is null
                ? Decode(bytes, decodePixelWidth, createOptions)
                : await dispatcher.InvokeAsync(() => Decode(bytes, decodePixelWidth, createOptions));
        }
        // The client is disposed at shutdown while late downloads can still be running.
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or
                                              ObjectDisposedException or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    private async Task<byte[]> DownloadAsync(Uri source, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        request.Headers.UserAgent.Add(UserAgent);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        // Throws HttpRequestException once the body passes the limit, so an oversized image is never fully buffered.
        await response.Content.LoadIntoBufferAsync(maxBytes, cancellation).ConfigureAwait(false);
        return await response.Content.ReadAsByteArrayAsync(cancellation).ConfigureAwait(false);
    }

    private static BitmapSource? Decode(byte[] bytes, int decodePixelWidth, BitmapCreateOptions createOptions)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = stream;
            if (decodePixelWidth > 0) image.DecodePixelWidth = decodePixelWidth;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = createOptions;
            image.EndInit();
            // Frozen, so a copy or save can use it from the app's thread.
            image.Freeze();
            return image;
        }
        catch (Exception exception) when (exception is NotSupportedException or IOException or ArgumentException or
                                              InvalidOperationException or ExternalException)
        {
            return null;
        }
    }
}
