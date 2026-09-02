namespace CircleToSearch.Tests;

using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Media;
using CircleToSearch.Translation;
using Xunit;
using Color = System.Windows.Media.Color;
using GdiBitmap = System.Drawing.Bitmap;
using GdiColor = System.Drawing.Color;

public sealed class DominantColorSamplerTests
{
    [Fact]
    public void SampleBackgroundColor_samples_edge_colors_accurately()
    {
        using var bitmap = new GdiBitmap(100, 100, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(GdiColor.FromArgb(30, 40, 50));
        }

        var sampled = DominantColorSampler.SampleBackgroundColor(bitmap, new Rect(10, 10, 40, 20), 1.0);
        Assert.Equal(30, sampled.R);
        Assert.Equal(40, sampled.G);
        Assert.Equal(50, sampled.B);
    }

    [Fact]
    public void GetContrastingTextColor_returns_white_for_dark_and_dark_for_light()
    {
        var darkBg = Color.FromRgb(20, 20, 20);
        var textColorForDark = DominantColorSampler.GetContrastingTextColor(darkBg);
        Assert.Equal(Colors.White, textColorForDark);

        var lightBg = Color.FromRgb(240, 240, 240);
        var textColorForLight = DominantColorSampler.GetContrastingTextColor(lightBg);
        Assert.Equal(Color.FromRgb(0x1F, 0x20, 0x23), textColorForLight);
    }
}
