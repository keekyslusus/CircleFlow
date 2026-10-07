namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CircleToSearch.Ui;

internal sealed class OverlayBackdropSampler(SelectionOverlayVisual selection)
{
    // A sparse grid averages to the same color as every pixel while keeping a 4K label region cheap.
    private const int SampleStep = 4;

    internal Color? AverageColorBehind(FrameworkElement element)
    {
        var screenshot = selection.Screenshot;
        if (screenshot.Source is not BitmapSource source || source.PixelWidth == 0 || source.PixelHeight == 0 ||
            screenshot.ActualWidth <= 0 || screenshot.ActualHeight <= 0 ||
            screenshot.Parent is not Visual root || !element.IsDescendantOf(root))
            return null;

        var bounds = element.TransformToVisual(screenshot).TransformBounds(new Rect(element.RenderSize));
        var scaleX = source.PixelWidth / screenshot.ActualWidth;
        var scaleY = source.PixelHeight / screenshot.ActualHeight;
        var left = Math.Clamp((int)Math.Floor(bounds.Left * scaleX), 0, source.PixelWidth);
        var top = Math.Clamp((int)Math.Floor(bounds.Top * scaleY), 0, source.PixelHeight);
        var right = Math.Clamp((int)Math.Ceiling(bounds.Right * scaleX), 0, source.PixelWidth);
        var bottom = Math.Clamp((int)Math.Ceiling(bounds.Bottom * scaleY), 0, source.PixelHeight);
        if (right <= left || bottom <= top) return null;

        var average = Average(source, new Int32Rect(left, top, right - left, bottom - top));
        var center = new Point(element.RenderSize.Width / 2, element.RenderSize.Height / 2);
        var dim = Math.Min(1, Coverage(selection.Dim, element, center) + Coverage(selection.DimRect, element, center));
        if (dim <= 0) return average;
        var dimColor = PluginPalette.SelectionDim;
        return PluginPalette.Composite(average, PluginPalette.WithAlpha(dimColor, dimColor.A / 255d * dim));
    }

    private static Color Average(BitmapSource frame, Int32Rect region)
    {
        var source = frame.Format == PixelFormats.Bgra32 || frame.Format == PixelFormats.Bgr32
            ? frame
            : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var row = new byte[region.Width * 4];
        long blue = 0, green = 0, red = 0, count = 0;
        for (var y = region.Y; y < region.Y + region.Height; y += SampleStep)
        {
            source.CopyPixels(new Int32Rect(region.X, y, region.Width, 1), row, row.Length, 0);
            for (var pixel = 0; pixel < row.Length; pixel += SampleStep * 4)
            {
                blue += row[pixel];
                green += row[pixel + 1];
                red += row[pixel + 2];
                count++;
            }
        }
        return Color.FromRgb((byte)(red / count), (byte)(green / count), (byte)(blue / count));
    }

    // The dim layers cross-fade when a selection is revealed, so their current opacities add up.
    private static double Coverage(Path layer, FrameworkElement element, Point center)
    {
        if (layer.Opacity <= 0 || layer.Visibility != Visibility.Visible || layer.Data is not { } data) return 0;
        return data.FillContains(element.TranslatePoint(center, layer)) ? layer.Opacity : 0;
    }
}
