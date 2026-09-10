using System.Drawing;
using System.Drawing.Imaging;
using CircleToSearch.Capture;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class ImageCropperTests
{
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

    [Fact]
    public void Crop_extracts_exact_pixels()
    {
        using var source = NewSplitBitmap(4, 4);

        var jpeg = ImageCropper.EncodeJpeg(source, new Rectangle(2, 0, 2, 4), maxLongSidePx: 1600);

        using var decoded = Decode(jpeg);
        Assert.Equal(2, decoded.Width);
        Assert.Equal(4, decoded.Height);
        for (var y = 0; y < 4; y++)
            for (var x = 0; x < 2; x++)
                AssertColor(decoded.GetPixel(x, y), Color.Blue);
    }

    [Fact]
    public void Crop_at_the_left_edge_keeps_left_pixels()
    {
        using var source = NewSplitBitmap(4, 4);

        var jpeg = ImageCropper.EncodeJpeg(source, new Rectangle(0, 0, 2, 4), maxLongSidePx: 1600);

        using var decoded = Decode(jpeg);
        Assert.Equal(2, decoded.Width);
        AssertColor(decoded.GetPixel(0, 0), Color.Red);
        AssertColor(decoded.GetPixel(1, 3), Color.Red);
    }

    [Fact]
    public void Crop_at_the_bottom_edge_keeps_bottom_pixels()
    {
        using var source = NewSplitBitmap(4, 4);

        var jpeg = ImageCropper.EncodeJpeg(source, new Rectangle(0, 2, 4, 2), maxLongSidePx: 1600);

        using var decoded = Decode(jpeg);
        Assert.Equal(2, decoded.Height);
        AssertColor(decoded.GetPixel(0, 0), Color.Red);
        AssertColor(decoded.GetPixel(3, 1), Color.Blue);
    }

    [Fact]
    public void Image_at_or_below_the_cap_is_not_resized()
    {
        using var source = NewSolidBitmap(8, 8, Color.Green);

        var jpeg = ImageCropper.EncodeJpeg(source, new Rectangle(0, 0, 8, 8), maxLongSidePx: 8);

        using var decoded = Decode(jpeg);
        Assert.Equal(8, decoded.Width);
        Assert.Equal(8, decoded.Height);
    }

    [Fact]
    public void Image_above_the_cap_is_downscaled_on_the_long_side()
    {
        using var source = NewSolidBitmap(20, 10, Color.Green);

        var jpeg = ImageCropper.EncodeJpeg(source, new Rectangle(0, 0, 20, 10), maxLongSidePx: 10);

        using var decoded = Decode(jpeg);
        Assert.Equal(10, decoded.Width);
        Assert.Equal(5, decoded.Height);
    }

    [Fact]
    public void Output_is_a_quality_95_jpeg()
    {
        using var source = NewSolidBitmap(4, 4, Color.Green);

        var jpeg = ImageCropper.EncodeJpeg(source, new Rectangle(0, 0, 4, 4), maxLongSidePx: 1600);

        Assert.Equal(95L, ImageCropper.JpegQuality);
        Assert.Equal(JpegSignature, jpeg[..JpegSignature.Length]);
        Assert.Equal(ImageFormat.Jpeg, Decode(jpeg).RawFormat);
    }

    private static Bitmap NewSplitBitmap(int width, int height)
    {
        var bitmap = new Bitmap(width, height);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                bitmap.SetPixel(x, y, x < width / 2 ? Color.Red : Color.Blue);
        return bitmap;
    }

    private static Bitmap NewSolidBitmap(int width, int height, Color color)
    {
        var bitmap = new Bitmap(width, height);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                bitmap.SetPixel(x, y, color);
        return bitmap;
    }

    private static void AssertColor(Color actual, Color expected)
    {
        Assert.InRange(actual.R, Math.Max(0, expected.R - 10), Math.Min(255, expected.R + 10));
        Assert.InRange(actual.G, Math.Max(0, expected.G - 10), Math.Min(255, expected.G + 10));
        Assert.InRange(actual.B, Math.Max(0, expected.B - 10), Math.Min(255, expected.B + 10));
    }

    private static Bitmap Decode(byte[] jpeg) => new(new MemoryStream(jpeg));
}
