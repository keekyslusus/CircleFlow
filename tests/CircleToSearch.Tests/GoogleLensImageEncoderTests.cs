using System.Drawing;
using System.Drawing.Imaging;
using CircleToSearch.Search;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class GoogleLensImageEncoderTests
{
    [Fact]
    public void EncodeJpeg_uses_quality_95_and_preserves_dimensions()
    {
        using var source = new Bitmap(32, 20);
        using (var graphics = Graphics.FromImage(source))
        {
            graphics.Clear(Color.MediumPurple);
            graphics.FillEllipse(Brushes.White, 4, 2, 24, 16);
        }

        using var png = new MemoryStream();
        source.Save(png, ImageFormat.Png);

        var encoded = GoogleLensImageEncoder.EncodeJpeg(png.ToArray());

        Assert.Equal(95L, GoogleLensImageEncoder.JpegQuality);
        Assert.Equal([0xFF, 0xD8, 0xFF], encoded[..3]);
        using var decoded = new Bitmap(new MemoryStream(encoded));
        Assert.Equal(ImageFormat.Jpeg, decoded.RawFormat);
        Assert.Equal(source.Size, decoded.Size);
    }
}
