using System.Drawing;
using CircleToSearch.Capture;
using CircleToSearch.Interop;
using CircleToSearch.Search;
using CircleToSearch.Search.Browser;
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

        using var bitmap = NewGradientBitmap(64, 64);
        var jpeg = ImageCropper.EncodeJpeg(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height), 1600);

        using var provider = new YandexImagesProvider();
        var outcome = await provider.PrepareAsync(jpeg, CancellationToken.None);

        Assert.True(outcome.Success, $"upload failed: {outcome.Failure} status {outcome.StatusCode}");
        Assert.True(YandexResultUrlPolicy.IsAllowed(outcome.PreparedSearch!.RequireResultsUrl()));
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task Uploaded_result_opens_in_the_shared_browser_host()
    {
        if (Environment.GetEnvironmentVariable("CTS_LIVE") != "1" ||
            Environment.GetEnvironmentVariable("CTS_WEBVIEW2_LIVE") != "1") return;

        var paths = new AppPaths(Path.Combine(TestOutputPaths.TempDirectory, "CircleFlow.WebView2Live"));
        AppDataDirectory.Initialize(paths);
        var environments = new WebViewEnvironmentFactory(paths, TestUiStrings.English, new TestPluginNotifier());
        using var provider = new YandexImagesProvider();
        using var bitmap = NewGradientBitmap(64, 64);
        var preparation = await provider.PrepareAsync(
            ImageCropper.EncodeJpeg(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height), 1600),
            CancellationToken.None);
        Assert.True(preparation.Success, $"upload failed: {preparation.Failure}");

        using var host = new SearchBrowserHost(
            AppContext.BaseDirectory,
            paths.SearchProfileDirectory,
            TestUiStrings.English,
            new PluginLog(paths.LogsDirectory),
            new StaDispatcher(CompositionRoot.SearchBrowserThreadName),
            () => environments.CreateAsync(paths.SearchProfileDirectory, enableExtensions: true));
        var shown = await host.ShowAsync(
            new SearchProviderDescriptor(SearchProviderIds.YandexImages, "Yandex Images"),
            preparation.PreparedSearch!,
            CancellationToken.None);

        Assert.Equal(SearchBrowserShowStatus.Shown, shown.Status);
    }

    private static Bitmap NewGradientBitmap(int width, int height)
    {
        var bitmap = new Bitmap(width, height);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                bitmap.SetPixel(x, y, Color.FromArgb(x * 4 % 256, y * 4 % 256, 128));
        return bitmap;
    }
}
