using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CircleToSearch.QrCodes;
using Xunit;
using Xunit.Abstractions;
using ZXing;
using ZXing.QrCode;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Tests;

public sealed class QrCodeScannerTests(ITestOutputHelper output)
{
    [Fact]
    public void Finds_every_code_on_a_4k_frame_with_its_bounds()
    {
        var frame = Frame(3840, 2160,
            ("https://example.com/first", new System.Drawing.Point(300, 200), 4),
            ("WIFI:T:WPA;S:Home;P:secret;;", new System.Drawing.Point(2600, 1500), 6));

        var watch = Stopwatch.StartNew();
        var matches = QrCodeScanner.Scan(frame, CancellationToken.None);
        output.WriteLine($"scan took {watch.ElapsedMilliseconds} ms");

        Assert.Equal(2, matches.Count);
        var first = Assert.Single(matches, match => match.Text == "https://example.com/first");
        var second = Assert.Single(matches, match => match.Text == "WIFI:T:WPA;S:Home;P:secret;;");
        AssertNear(Placed("https://example.com/first", new(300, 200), 4), first.Bounds);
        AssertNear(Placed("WIFI:T:WPA;S:Home;P:secret;;", new(2600, 1500), 6), second.Bounds);
    }

    [Fact]
    public void Finds_a_code_scaled_by_a_fractional_factor_with_smoothing()
    {
        var source = Frame(300, 300, ("https://example.com/scaled", new System.Drawing.Point(100, 100), 3));
        var scaled = new TransformedBitmap(source, new ScaleTransform(1.37, 1.37));
        var frame = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        frame.Freeze();

        Assert.Equal("https://example.com/scaled", Assert.Single(QrCodeScanner.Scan(frame, CancellationToken.None)).Text);
    }

    [Fact]
    public void Frame_without_codes_returns_nothing()
    {
        Assert.Empty(QrCodeScanner.Scan(Frame(640, 360), CancellationToken.None));
    }

    [Fact]
    public void Canceled_scan_throws()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            QrCodeScanner.Scan(Frame(640, 360), cancellation.Token));
    }

    // The symbol area without its quiet zone, which the scanner cannot see against a white page.
    private static GdiRectangle Placed(string text, System.Drawing.Point origin, int module)
    {
        var matrix = new QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, 0, 0,
            new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = 0 });
        return new GdiRectangle(origin.X, origin.Y, matrix.Width * module, matrix.Height * module);
    }

    private static void AssertNear(GdiRectangle expected, GdiRectangle actual)
    {
        const int tolerance = 6;
        Assert.InRange(actual.Left, expected.Left - tolerance, expected.Left + tolerance);
        Assert.InRange(actual.Top, expected.Top - tolerance, expected.Top + tolerance);
        Assert.InRange(actual.Right, expected.Right - tolerance, expected.Right + tolerance);
        Assert.InRange(actual.Bottom, expected.Bottom - tolerance, expected.Bottom + tolerance);
    }

    internal static BitmapSource Frame(int width, int height,
        params (string Text, System.Drawing.Point Origin, int Module)[] codes)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        Array.Fill(pixels, (byte)0xFF);
        foreach (var (text, origin, module) in codes)
        {
            var matrix = new QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, 0, 0,
                new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = 0 });
            for (var y = 0; y < matrix.Height * module; y++)
            for (var x = 0; x < matrix.Width * module; x++)
            {
                if (!matrix[x / module, y / module]) continue;
                var offset = (origin.Y + y) * stride + (origin.X + x) * 4;
                pixels[offset] = pixels[offset + 1] = pixels[offset + 2] = 0;
            }
        }
        var frame = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        frame.Freeze();
        return frame;
    }
}
