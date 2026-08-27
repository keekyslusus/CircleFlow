using System.Drawing;
using System.Drawing.Imaging;
using CircleToSearch.Capture;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class ImageCropperTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public void Crop_extracts_exact_pixels()
    {
        using var source = NewSplitBitmap(4, 4);

        var png = ImageCropper.Encode(source, new Rectangle(2, 0, 2, 4), maxLongSidePx: 1600);

        using var decoded = Decode(png);
        Assert.Equal(2, decoded.Width);
        Assert.Equal(4, decoded.Height);
        for (var y = 0; y < 4; y++)
            for (var x = 0; x < 2; x++)
                AssertArgb(decoded.GetPixel(x, y), Color.Blue);
    }

    [Fact]
    public void Crop_at_the_left_edge_keeps_left_pixels()
    {
        using var source = NewSplitBitmap(4, 4);

        var png = ImageCropper.Encode(source, new Rectangle(0, 0, 2, 4), maxLongSidePx: 1600);

        using var decoded = Decode(png);
        Assert.Equal(2, decoded.Width);
        AssertArgb(decoded.GetPixel(0, 0), Color.Red);
        AssertArgb(decoded.GetPixel(1, 3), Color.Red);
    }

    [Fact]
    public void Crop_at_the_bottom_edge_keeps_bottom_pixels()
    {
        using var source = NewSplitBitmap(4, 4);

        var png = ImageCropper.Encode(source, new Rectangle(0, 2, 4, 2), maxLongSidePx: 1600);

        using var decoded = Decode(png);
        Assert.Equal(2, decoded.Height);
        AssertArgb(decoded.GetPixel(0, 0), Color.Red);
        AssertArgb(decoded.GetPixel(3, 1), Color.Blue);
    }

    [Fact]
    public void Image_at_or_below_the_cap_is_not_resized()
    {
        using var source = NewSolidBitmap(8, 8, Color.Green);

        var png = ImageCropper.Encode(source, new Rectangle(0, 0, 8, 8), maxLongSidePx: 8);

        using var decoded = Decode(png);
        Assert.Equal(8, decoded.Width);
        Assert.Equal(8, decoded.Height);
    }

    [Fact]
    public void Image_above_the_cap_is_downscaled_on_the_long_side()
    {
        using var source = NewSolidBitmap(20, 10, Color.Green);

        var png = ImageCropper.Encode(source, new Rectangle(0, 0, 20, 10), maxLongSidePx: 10);

        using var decoded = Decode(png);
        Assert.Equal(10, decoded.Width);
        Assert.Equal(5, decoded.Height);
    }

    [Fact]
    public void Output_is_a_valid_png()
    {
        using var source = NewSolidBitmap(4, 4, Color.Green);

        var png = ImageCropper.Encode(source, new Rectangle(0, 0, 4, 4), maxLongSidePx: 1600);

        Assert.Equal(PngSignature, png[..PngSignature.Length]);
        Assert.Equal(ImageFormat.Png, Decode(png).RawFormat);
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

    private static void AssertArgb(Color actual, Color expected)
        => Assert.Equal(expected.ToArgb(), actual.ToArgb());

    private static Bitmap Decode(byte[] png) => new(new MemoryStream(png));
}
