using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Capture;

internal static class VisibleImage
{
    internal static BitmapSource Crop(BitmapSource source, GdiRectangle bounds)
    {
        var crop = new CroppedBitmap(source, new Int32Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        crop.Freeze();
        return crop;
    }

    internal static BitmapSource ReplaceRegion(BitmapSource source, BitmapSource replacement, GdiRectangle bounds)
    {
        var scaled = new TransformedBitmap(replacement, new ScaleTransform(
            (double)bounds.Width / replacement.PixelWidth, (double)bounds.Height / replacement.PixelHeight));
        var pixels = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        var stride = checked(bounds.Width * 4);
        var buffer = new byte[checked(stride * bounds.Height)];
        pixels.CopyPixels(buffer, stride, 0);
        var output = new WriteableBitmap(new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0));
        output.WritePixels(new Int32Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height), buffer, stride, 0);
        output.Freeze();
        return output;
    }

    internal static SelectionOutcome CreateSelection(BitmapSource source, GdiRectangle bounds)
    {
        var encoder = new BmpBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Crop(source, bounds)));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        using var decoded = new System.Drawing.Bitmap(stream);
        return new SelectionOutcome(new GdiRectangle(0, 0, bounds.Width, bounds.Height),
            new System.Drawing.Bitmap(decoded));
    }
}
