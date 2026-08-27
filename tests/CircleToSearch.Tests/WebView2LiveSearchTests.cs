using System.Drawing;
using System.Drawing.Imaging;
using CircleToSearch.Search;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class WebView2LiveSearchTests
{
    [Fact]
    [Trait("Category", "Live")]
    public async Task Google_lens_opens_results_for_an_in_memory_png()
    {
        if (Environment.GetEnvironmentVariable("CTS_WEBVIEW2_LIVE") != "1") return;

        var dataDirectory = Path.Combine(Path.GetTempPath(), "CircleToSearch.WebView2Live");
        using var window = new WebView2SearchWindow(
            AppContext.BaseDirectory,
            Path.Combine(dataDirectory, "Profile"),
            new PluginLog(dataDirectory));

        var first = await window.ShowAsync(CreatePng(), CancellationToken.None);
        var second = await window.ShowAsync(CreatePng(), CancellationToken.None);

        Assert.Equal(WebView2SearchStatus.ResultsReady, first);
        Assert.Equal(WebView2SearchStatus.ResultsReady, second);
    }

    private static byte[] CreatePng()
    {
        using var bitmap = new Bitmap(96, 96);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.MediumPurple);
        graphics.FillEllipse(Brushes.White, 20, 20, 56, 56);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
