using System.Buffers;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;
using ZXing.Multi.QrCode;
using ZXing.QrCode.Internal;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.QrCodes;

internal sealed record QrCodeMatch(string Text, GdiRectangle Bounds);

internal static class QrCodeScanner
{
    internal const int MaxMatches = 4;
    // A finder pattern center sits 3.5 modules inside the symbol edge.
    private const double FinderCenterInsetModules = 3.5;

    private static readonly IDictionary<DecodeHintType, object> Hints = new Dictionary<DecodeHintType, object>
    {
        [DecodeHintType.POSSIBLE_FORMATS] = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
        [DecodeHintType.TRY_HARDER] = true,
    };

    internal static IReadOnlyList<QrCodeMatch> Scan(BitmapSource frame, CancellationToken cancellation)
    {
        // A 4K frame needs 8 MB of luminance on every overlay open, so the buffer comes from the pool.
        var luminance = ArrayPool<byte>.Shared.Rent(frame.PixelWidth * frame.PixelHeight);
        Result[]? results;
        try
        {
            FillLuminance(frame, luminance, cancellation);
            cancellation.ThrowIfCancellationRequested();
            var source = new PlanarYUVLuminanceSource(
                luminance, frame.PixelWidth, frame.PixelHeight, 0, 0, frame.PixelWidth, frame.PixelHeight, false);
            try { results = new QRCodeMultiReader().decodeMultiple(new BinaryBitmap(new HybridBinarizer(source)), Hints); }
            catch (ReaderException) { results = null; }
        }
        finally { ArrayPool<byte>.Shared.Return(luminance); }
        cancellation.ThrowIfCancellationRequested();
        if (results is null) return [];

        var matches = new List<QrCodeMatch>(Math.Min(results.Length, MaxMatches));
        foreach (var result in results)
        {
            if (string.IsNullOrWhiteSpace(result.Text) || Bounds(result, frame) is not { } bounds) continue;
            if (matches.Any(match => match.Bounds.IntersectsWith(bounds))) continue;
            matches.Add(new QrCodeMatch(result.Text, bounds));
            if (matches.Count == MaxMatches) break;
        }
        return matches;
    }

    // Copying one row at a time keeps a 4K frame from needing a second full-size BGRA buffer.
    private static void FillLuminance(BitmapSource frame, byte[] luminance, CancellationToken cancellation)
    {
        var source = frame.Format == PixelFormats.Bgra32 || frame.Format == PixelFormats.Bgr32
            ? frame
            : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var row = new byte[width * 4];
        for (var y = 0; y < height; y++)
        {
            if ((y & 0xFF) == 0) cancellation.ThrowIfCancellationRequested();
            source.CopyPixels(new Int32Rect(0, y, width, 1), row, row.Length, 0);
            var offset = y * width;
            for (var x = 0; x < width; x++)
            {
                var pixel = x * 4;
                luminance[offset + x] = (byte)((row[pixel] * 29 + row[pixel + 1] * 150 + row[pixel + 2] * 77) >> 8);
            }
        }
    }

    private static GdiRectangle? Bounds(Result result, BitmapSource frame)
    {
        var points = result.ResultPoints;
        if (points is null || points.Length < 3) return null;
        var (bottomLeft, topLeft, topRight) = (points[0], points[1], points[2]);
        var corners = new[]
        {
            bottomLeft, topLeft, topRight,
            new ResultPoint(topRight.X + bottomLeft.X - topLeft.X, topRight.Y + bottomLeft.Y - topLeft.Y),
        };
        var module = new[] { bottomLeft, topLeft, topRight }.OfType<FinderPattern>()
            .Select(pattern => pattern.EstimatedModuleSize).DefaultIfEmpty(0).Average();
        var inset = FinderCenterInsetModules * module;
        var left = corners.Min(point => point.X) - inset;
        var top = corners.Min(point => point.Y) - inset;
        var right = corners.Max(point => point.X) + inset;
        var bottom = corners.Max(point => point.Y) + inset;
        var bounds = GdiRectangle.FromLTRB(
            (int)Math.Floor(Math.Max(0, left)),
            (int)Math.Floor(Math.Max(0, top)),
            (int)Math.Ceiling(Math.Min(frame.PixelWidth, right)),
            (int)Math.Ceiling(Math.Min(frame.PixelHeight, bottom)));
        return bounds.Width > 0 && bounds.Height > 0 ? bounds : null;
    }
}
