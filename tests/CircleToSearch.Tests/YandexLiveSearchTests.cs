using System.Drawing;
using System.Drawing.Imaging;
using CircleToSearch.Search;
using Xunit;

namespace CircleToSearch.Tests;

// Opt-in: runs against the live Yandex endpoint only with
//   dotnet test --filter "Category=Live" -e CTS_LIVE=1
public sealed class YandexLiveSearchTests
{
    [Fact]
    [Trait("Category", "Live")]
    public async Task Upload_returns_a_yandex_results_url()
    {
        if (Environment.GetEnvironmentVariable("CTS_LIVE") != "1") return;

        var png = EncodePng(NewGradientBitmap(64, 64));

        var outcome = await new YandexImagesProvider().SearchAsync(png, CancellationToken.None);

        Assert.True(outcome.Success, $"upload failed: {outcome.Failure} status {outcome.StatusCode}");
        Assert.True(YandexResultUrlPolicy.IsAllowed(new Uri(outcome.ResultsUrl!)));
    }

    private static Bitmap NewGradientBitmap(int width, int height)
    {
        var bitmap = new Bitmap(width, height);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                bitmap.SetPixel(x, y, Color.FromArgb(x * 4 % 256, y * 4 % 256, 128));
        return bitmap;
    }

    private static byte[] EncodePng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
