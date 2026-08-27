namespace CircleToSearch.Capture;

using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

public static class ImageCropper
{
    public static byte[] Encode(Bitmap source, Rectangle rect, int maxLongSidePx)
    {
        using var cropped = source.Clone(rect, source.PixelFormat);
        var longSide = Math.Max(cropped.Width, cropped.Height);
        if (longSide <= maxLongSidePx) return EncodePng(cropped);

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
        return EncodePng(resized);
    }

    private static byte[] EncodePng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
