namespace CircleToSearch.Translation;

using System.Windows;
using System.Windows.Media;
using GdiBitmap = System.Drawing.Bitmap;

public static class DominantColorSampler
{
    public static Color SampleBackgroundColor(GdiBitmap frame, Rect dipBounds, double scale)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (scale <= 0) scale = 1.0;

        var px = Math.Clamp((int)Math.Round(dipBounds.X * scale), 0, frame.Width - 1);
        var py = Math.Clamp((int)Math.Round(dipBounds.Y * scale), 0, frame.Height - 1);
        var pw = Math.Clamp((int)Math.Round(dipBounds.Width * scale), 1, frame.Width - px);
        var ph = Math.Clamp((int)Math.Round(dipBounds.Height * scale), 1, frame.Height - py);

        (int x, int y)[] points =
        [
            (px + 1, py + 1),
            (px + Math.Max(1, pw - 2), py + 1),
            (px + 1, py + Math.Max(1, ph - 2)),
            (px + Math.Max(1, pw - 2), py + Math.Max(1, ph - 2)),
            (px + pw / 2, py + 1),
            (px + pw / 2, py + Math.Max(1, ph - 2)),
        ];

        long rSum = 0, gSum = 0, bSum = 0;
        var count = 0;
        foreach (var (x, y) in points)
        {
            if (x >= 0 && x < frame.Width && y >= 0 && y < frame.Height)
            {
                var pixel = frame.GetPixel(x, y);
                rSum += pixel.R;
                gSum += pixel.G;
                bSum += pixel.B;
                count++;
            }
        }

        if (count == 0) return Color.FromRgb(0x20, 0x21, 0x24);
        return Color.FromRgb((byte)(rSum / count), (byte)(gSum / count), (byte)(bSum / count));
    }

    public static Color GetContrastingTextColor(Color background)
    {
        var luminance = 0.299 * background.R + 0.587 * background.G + 0.114 * background.B;
        return luminance < 128 ? Colors.White : Color.FromRgb(0x1F, 0x20, 0x23);
    }
}
