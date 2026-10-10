using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using CircleToSearch.Ui;

namespace CircleToSearch.Search;

public enum PinterestDebugMode
{
    Live,
    Simulated,
}

// Debug: answers Pinterest's visual search and its pin images locally, so the widget can be exercised as often as
// needed without calling Pinterest. Requests go through the real provider and image loader; only the network is
// replaced. Everything else, and Pinterest itself while the simulation is off, still goes to the network.
internal sealed class PinterestSimulation(UiStrings strings, TimeSpan? searchDelay = null, TimeSpan? maxImageDelay = null)
{
    private const string ImageHost = "i.pinimg.com";
    private const string ImagePathPrefix = "/simulated/";
    private const int PreviewWidth = 474;
    private const int FullWidth = 1200;
    // Width-to-height ratios of real results: mostly portrait, some square and landscape.
    private static readonly double[] AspectRatios =
        [0.6, 0.75, 1.0, 1.33, 0.5, 0.8, 1.0, 0.66, 1.5, 0.7, 0.9, 1.2, 0.56, 0.75, 1.0, 0.62, 0.8, 1.1, 0.7, 0.5, 1.0, 0.85];

    private readonly TimeSpan _searchDelay = searchDelay ?? TimeSpan.FromMilliseconds(800);
    private readonly TimeSpan _maxImageDelay = maxImageDelay ?? TimeSpan.FromMilliseconds(1500);
    private readonly ConcurrentDictionary<(int Index, int Width), byte[]> _images = new();
    private volatile bool _enabled;

    internal PinterestDebugMode Mode
    {
        get => _enabled ? PinterestDebugMode.Simulated : PinterestDebugMode.Live;
        set => _enabled = value == PinterestDebugMode.Simulated;
    }

    internal static int PinCount => AspectRatios.Length;

    internal HttpMessageHandler CreateHandler(HttpMessageHandler network) => new Handler(this, network);

    internal string SearchResponse()
    {
        var pins = AspectRatios.Select((ratio, index) => new Dictionary<string, object>
        {
            ["type"] = "pin",
            ["id"] = (900000000000000000L + index).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["title"] = strings.DebugPinterestPinTitle(index + 1),
            ["domain"] = "example.com",
            ["link"] = $"https://example.com/{index + 1}",
            ["image_medium_url"] = ImageUrl(index, PreviewWidth),
            ["image_large_url"] = ImageUrl(index, FullWidth),
            ["image_medium_size_pixels"] = new { width = PreviewWidth, height = Height(index, PreviewWidth) },
        });
        return JsonSerializer.Serialize(new { status = "success", data = pins });
    }

    private static string ImageUrl(int index, int width) => $"https://{ImageHost}{ImagePathPrefix}{width}/{index}.jpg";

    private static int Height(int index, int width) => (int)Math.Round(width / AspectRatios[index]);

    private static bool IsSimulatedImage(Uri uri) =>
        uri.Host == ImageHost && uri.AbsolutePath.StartsWith(ImagePathPrefix, StringComparison.Ordinal);

    private static bool TryParseImage(Uri uri, out int index, out int width)
    {
        index = width = 0;
        var parts = uri.AbsolutePath[ImagePathPrefix.Length..].Split('/');
        return parts.Length == 2 && int.TryParse(parts[0], out width) && width is PreviewWidth or FullWidth &&
               int.TryParse(Path.GetFileNameWithoutExtension(parts[1]), out index) && index >= 0 &&
               index < AspectRatios.Length;
    }

    private byte[] Image(int index, int width) => _images.GetOrAdd((index, width), key =>
    {
        var height = Height(key.Index, key.Width);
        using var bitmap = new Bitmap(key.Width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            var random = new Random(key.Index);
            Color Next(int alpha) => Color.FromArgb(alpha, random.Next(40, 220), random.Next(40, 220), random.Next(40, 220));
            using var gradient = new LinearGradientBrush(new Rectangle(0, 0, key.Width, height), Next(255), Next(255), 45);
            graphics.FillRectangle(gradient, 0, 0, key.Width, height);
            // The same shapes at either width, so a copied full image matches its preview.
            var scale = key.Width / (double)PreviewWidth;
            for (var shape = 0; shape < 40; shape++)
            {
                using var fill = new SolidBrush(Next(90));
                var size = (float)(random.Next(20, 160) * scale);
                graphics.FillEllipse(fill, (float)(random.Next(PreviewWidth) * scale),
                    (float)(random.Next(Height(key.Index, PreviewWidth)) * scale), size, size);
            }
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Jpeg);
        return stream.ToArray();
    });

    private sealed class Handler(PinterestSimulation simulation, HttpMessageHandler network) : DelegatingHandler(network)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri;
            if (uri is not null && IsSimulatedImage(uri))
            {
                if (!TryParseImage(uri, out var index, out var width))
                    return new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request };
                // Each image arrives after its own delay, as it would from the network.
                var delay = simulation._maxImageDelay.TotalMilliseconds;
                if (delay > 0) await Task.Delay(Random.Shared.Next((int)delay), cancellationToken).ConfigureAwait(false);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new ByteArrayContent(simulation.Image(index, width)),
                };
            }
            if (simulation._enabled && uri?.AbsoluteUri == PinterestProvider.Endpoint)
            {
                if (simulation._searchDelay > TimeSpan.Zero)
                    await Task.Delay(simulation._searchDelay, cancellationToken).ConfigureAwait(false);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new StringContent(simulation.SearchResponse(), Encoding.UTF8, "application/json"),
                };
            }
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
