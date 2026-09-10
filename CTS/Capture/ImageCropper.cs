namespace CircleToSearch.Capture;

using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

public static class ImageCropper
{
    internal const long JpegQuality = 95L;

    public static byte[] EncodeJpeg(Bitmap source, Rectangle rect, int maxLongSidePx)
    {
        using var cropped = source.Clone(rect, source.PixelFormat);
        var longSide = Math.Max(cropped.Width, cropped.Height);
        if (longSide <= maxLongSidePx) return EncodeJpeg(cropped);

        var scale = (double)maxLongSidePx / longSide;
        var width = Math.Max(1, (int)Math.Round(cropped.Width * scale));
        var height = Math.Max(1, (int)Math.Round(cropped.Height * scale));
        using var resized = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(cropped, new Rectangle(0, 0, width, height));
        }
        return EncodeJpeg(resized);
    }

    private static byte[] EncodeJpeg(Bitmap source)
    {
        using var jpeg = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(jpeg))
        {
            graphics.Clear(Color.White);
            graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        }

        var codec = ImageCodecInfo.GetImageEncoders()
            .Single(encoder => encoder.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, JpegQuality);
        using var output = new MemoryStream();
        jpeg.Save(output, codec, parameters);
        return output.ToArray();
    }
}
