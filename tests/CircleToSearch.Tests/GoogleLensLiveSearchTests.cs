using System.Drawing;
using CircleToSearch.Capture;
using CircleToSearch.Interop;
using CircleToSearch.Search;
using CircleToSearch.Search.Browser;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class GoogleLensLiveSearchTests
{
    [Fact]
    [Trait("Category", "Live")]
    public async Task Google_lens_opens_results_for_an_in_memory_jpeg()
    {
        if (Environment.GetEnvironmentVariable("CTS_WEBVIEW2_LIVE") != "1") return;

        var paths = new AppPaths(Path.Combine(TestOutputPaths.TempDirectory, "CircleFlow.WebView2Live"));
        AppDataDirectory.Initialize(paths);
        var environments = new WebViewEnvironmentFactory(paths, TestUiStrings.English, new TestPluginNotifier());
        var log = new PluginLog(paths.LogsDirectory);
        using var host = new SearchBrowserHost(
            AppContext.BaseDirectory,
            paths.SearchProfileDirectory,
            log,
            new StaDispatcher(CompositionRoot.SearchBrowserThreadName),
            () => environments.CreateAsync(paths.SearchProfileDirectory, enableExtensions: true),
            (content, anchor, lightTheme) => CompositionRoot.CreateSearchBrowserWindowView(
                TestUiStrings.English, content, anchor, lightTheme));
        var provider = new GoogleLensProvider(
            jpeg => new GoogleLensBrowserOperation(jpeg, log));

        var firstPreparation = await provider.PrepareAsync(CreateJpeg(), CancellationToken.None);
        var first = await host.ShowAsync(
            new SearchProviderDescriptor(SearchProviderIds.GoogleLens, "Google Lens"),
            firstPreparation.PreparedSearch!,
            CancellationToken.None);
        var secondPreparation = await provider.PrepareAsync(CreateJpeg(), CancellationToken.None);
        var second = await host.ShowAsync(
            new SearchProviderDescriptor(SearchProviderIds.GoogleLens, "Google Lens"),
            secondPreparation.PreparedSearch!,
            CancellationToken.None);

        Assert.Equal(SearchBrowserShowStatus.Shown, first.Status);
        Assert.Equal(SearchBrowserShowStatus.Shown, second.Status);
        if (Environment.GetEnvironmentVariable("CTS_WEBVIEW2_PREVIEW") == "1")
            await Task.Delay(TimeSpan.FromSeconds(45));
    }

    [Fact]
    [Trait("Category", "Live")]
    public async Task Google_ai_mode_answers_a_question_about_an_in_memory_jpeg()
    {
        if (Environment.GetEnvironmentVariable("CTS_WEBVIEW2_LIVE") != "1") return;

        var paths = new AppPaths(Path.Combine(TestOutputPaths.TempDirectory, "CircleFlow.WebView2Live"));
        AppDataDirectory.Initialize(paths);
        var environments = new WebViewEnvironmentFactory(paths, TestUiStrings.English, new TestPluginNotifier());
        var log = new PluginLog(paths.LogsDirectory);
        using var host = new SearchBrowserHost(
            AppContext.BaseDirectory,
            paths.SearchProfileDirectory,
            log,
            new StaDispatcher(CompositionRoot.SearchBrowserThreadName),
            () => environments.CreateAsync(paths.SearchProfileDirectory, enableExtensions: true),
            (content, anchor, lightTheme) => CompositionRoot.CreateSearchBrowserWindowView(
                TestUiStrings.English, content, anchor, lightTheme));
        var answer = new AnswerProbe(new GoogleLensBrowserOperation(
            CreateShapesJpeg(), log, "What color is the circle? Answer with one word."));

        var shown = await host.ShowAsync(
            new SearchProviderDescriptor(SearchProviderIds.GoogleLens, "Google Lens"),
            PreparedVisualSearch.ForBrowserOperation(answer, externalFallbackUrl: null),
            CancellationToken.None);

        Assert.Equal(SearchBrowserShowStatus.Shown, shown.Status);
        Assert.Contains("udm=50", answer.FinalUri?.Query, StringComparison.Ordinal);
        Assert.True(answer.Answered, "AI Mode did not show an answer naming the circle color.");
        if (Environment.GetEnvironmentVariable("CTS_WEBVIEW2_PREVIEW") == "1")
            await Task.Delay(TimeSpan.FromSeconds(45));
    }

    private sealed class AnswerProbe(IVisualSearchBrowserOperation inner) : IVisualSearchBrowserOperation
    {
        public Uri? FinalUri { get; private set; }
        public bool Answered { get; private set; }

        public async Task<VisualSearchBrowserOperationStatus> ExecuteAsync(
            IVisualSearchBrowserSession session,
            CancellationToken cancel)
        {
            var status = await inner.ExecuteAsync(session, cancel);
            FinalUri = session.CurrentUri;
            for (var attempt = 0; attempt < 60 && status == VisualSearchBrowserOperationStatus.Succeeded; attempt++)
            {
                var text = System.Text.Json.JsonSerializer.Deserialize<string>(
                    await session.ExecuteScriptAsync("document.body?.innerText ?? ''", cancel)) ?? "";
                if (text.Contains("blue", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("син", StringComparison.OrdinalIgnoreCase))
                {
                    Answered = true;
                    break;
                }
                await Task.Delay(500, cancel);
            }
            return status;
        }
    }

    private static byte[] CreateShapesJpeg()
    {
        using var bitmap = new Bitmap(600, 400);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        graphics.FillRectangle(Brushes.Red, 60, 100, 200, 200);
        graphics.FillEllipse(Brushes.Blue, 340, 100, 200, 200);
        return ImageCropper.EncodeJpeg(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height), 1600);
    }

    private static byte[] CreateJpeg()
    {
        using var bitmap = new Bitmap(96, 96);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.MediumPurple);
        graphics.FillEllipse(Brushes.White, 20, 20, 56, 56);
        return ImageCropper.EncodeJpeg(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height), 1600);
    }
}
