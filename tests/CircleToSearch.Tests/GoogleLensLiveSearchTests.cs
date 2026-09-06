using System.Drawing;
using System.Drawing.Imaging;
using CircleToSearch.Interop;
using CircleToSearch.Search;
using CircleToSearch.Search.Browser;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class GoogleLensLiveSearchTests
{
    [Fact]
    [Trait("Category", "Live")]
    public async Task Google_lens_opens_results_for_an_in_memory_png()
    {
        if (Environment.GetEnvironmentVariable("CTS_WEBVIEW2_LIVE") != "1") return;

        var dataDirectory = Path.Combine(Path.GetTempPath(), "CircleToSearch.WebView2Live");
        var log = new PluginLog(dataDirectory);
        using var host = new SearchBrowserHost(
            AppContext.BaseDirectory,
            Path.Combine(dataDirectory, "Profile"),
            TestUiStrings.English,
            log,
            new StaDispatcher(CompositionRoot.SearchBrowserThreadName));
        var provider = new GoogleLensProvider(
            png => new GoogleLensBrowserOperation(png, log));

        var firstPreparation = await provider.PrepareAsync(CreatePng(), CancellationToken.None);
        var first = await host.ShowAsync(
            new SearchProviderDescriptor(SearchProviderIds.GoogleLens, "Google Lens"),
            firstPreparation.PreparedSearch!,
            CancellationToken.None);
        var secondPreparation = await provider.PrepareAsync(CreatePng(), CancellationToken.None);
        var second = await host.ShowAsync(
            new SearchProviderDescriptor(SearchProviderIds.GoogleLens, "Google Lens"),
            secondPreparation.PreparedSearch!,
            CancellationToken.None);

        Assert.Equal(SearchBrowserShowStatus.Shown, first.Status);
        Assert.Equal(SearchBrowserShowStatus.Shown, second.Status);
        if (Environment.GetEnvironmentVariable("CTS_WEBVIEW2_PREVIEW") == "1")
            await Task.Delay(TimeSpan.FromSeconds(45));
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
