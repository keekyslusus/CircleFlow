using System.Drawing;
using System.Text.Json;
using CircleToSearch.Capture;
using CircleToSearch.Interop;
using CircleToSearch.Search;
using CircleToSearch.Search.Browser;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class GoogleLensLiveSearchTests
{
    private static readonly SearchProviderDescriptor GoogleLens = new(SearchProviderIds.GoogleLens, "Google Lens");

    [SkippableFact]
    [Trait("Category", "Live")]
    public async Task Google_lens_opens_results_for_an_in_memory_jpeg()
    {
        TestSwitches.Require("CTS_WEBVIEW2_LIVE");

        var paths = new AppPaths(Path.Combine(TestOutputPaths.TempDirectory, "CircleFlow.WebView2Live"));
        AppDataDirectory.Initialize(paths);
        var environments = new WebViewEnvironmentFactory(paths, TestUiStrings.English, new TestPluginNotifier());
        var log = new PluginLog(paths.LogsDirectory);
        using var host = new SearchBrowserHost(
            new AppPaths().ExtensionArchivePath,
            new AppPaths().CosmeticFiltersPath,
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

    [SkippableFact]
    [Trait("Category", "Live")]
    public async Task Google_ai_mode_answers_a_question_about_an_in_memory_jpeg()
    {
        TestSwitches.Require("CTS_WEBVIEW2_LIVE");

        var paths = new AppPaths(Path.Combine(TestOutputPaths.TempDirectory, "CircleFlow.WebView2Live"));
        AppDataDirectory.Initialize(paths);
        var environments = new WebViewEnvironmentFactory(paths, TestUiStrings.English, new TestPluginNotifier());
        var log = new PluginLog(paths.LogsDirectory);
        using var host = new SearchBrowserHost(
            new AppPaths().ExtensionArchivePath,
            new AppPaths().CosmeticFiltersPath,
            paths.SearchProfileDirectory,
            log,
            new StaDispatcher(CompositionRoot.SearchBrowserThreadName),
            () => environments.CreateAsync(paths.SearchProfileDirectory, enableExtensions: true),
            (content, anchor, lightTheme) => CompositionRoot.CreateSearchBrowserWindowView(
                TestUiStrings.English, content, anchor, lightTheme));
        const string question = "What color is the circle? Answer with one word.";
        var jpeg = CreateShapesJpeg();
        var fallback = new FallbackProbe(new GoogleLensBrowserOperation(jpeg, log, question, GoogleLens));
        var answer = new AnswerProbe(new GoogleAiModeBrowserOperation(
            Task.FromResult(jpeg), Task.FromResult(question), (_, _) => fallback, log));

        var shown = await host.ShowAsync(
            new SearchProviderDescriptor(SearchProviderIds.GoogleAiMode, "Google AI Mode"),
            PreparedVisualSearch.ForBrowserOperation(answer, externalFallbackUrl: null),
            CancellationToken.None);

        Assert.Equal(SearchBrowserShowStatus.Shown, shown.Status);
        Assert.Contains("udm=50", answer.FinalUri?.Query, StringComparison.Ordinal);
        Assert.True(answer.Answered, "AI Mode did not show an answer naming the circle color.");
        Assert.False(fallback.Used, "The AI Mode page did not accept the question; the Lens fallback answered.");
        if (Environment.GetEnvironmentVariable("CTS_WEBVIEW2_PREVIEW") == "1")
            await Task.Delay(TimeSpan.FromSeconds(45));
    }

    [SkippableFact]
    [Trait("Category", "Live")]
    public async Task Google_ai_mode_prewarms_hidden_and_submits_right_after_the_question()
    {
        TestSwitches.Require("CTS_WEBVIEW2_LIVE");

        var paths = new AppPaths(Path.Combine(TestOutputPaths.TempDirectory, "CircleFlow.WebView2Live"));
        AppDataDirectory.Initialize(paths);
        var environments = new WebViewEnvironmentFactory(paths, TestUiStrings.English, new TestPluginNotifier());
        var log = new PluginLog(paths.LogsDirectory);
        using var host = new SearchBrowserHost(
            new AppPaths().ExtensionArchivePath,
            new AppPaths().CosmeticFiltersPath,
            paths.SearchProfileDirectory,
            log,
            new StaDispatcher(CompositionRoot.SearchBrowserThreadName),
            () => environments.CreateAsync(paths.SearchProfileDirectory, enableExtensions: true),
            (content, anchor, lightTheme) => CompositionRoot.CreateSearchBrowserWindowView(
                TestUiStrings.English, content, anchor, lightTheme));
        const string question = "What color is the square? Answer with one word.";
        var image = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var asked = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fallback = new FallbackProbe(new GoogleLensBrowserOperation(CreateShapesJpeg(), log, question, GoogleLens));
        var chips = new AttachmentCountingOperation(
            new GoogleAiModeBrowserOperation(image.Task, asked.Task, (_, _) => fallback, log));
        var answer = new AnswerProbe(chips, "red", "красн");

        var show = host.ShowAsync(
            new SearchProviderDescriptor(SearchProviderIds.GoogleAiMode, "Google AI Mode"),
            PreparedVisualSearch.ForBrowserOperation(answer, externalFallbackUrl: null, revealAfter: asked.Task),
            CancellationToken.None);
        // Typing starts before the hidden page has finished loading its scripts.
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        image.SetResult(CreateShapesJpeg());
        await Task.Delay(TimeSpan.FromSeconds(2));
        var askedAt = DateTime.UtcNow;
        asked.SetResult(question);
        var shown = await show;

        var submittedAfter = answer.SubmittedAt - askedAt;
        log.Info(nameof(GoogleLensLiveSearchTests), $"prewarmed question submitted after {submittedAfter.TotalMilliseconds:F0} ms");
        Assert.Equal(SearchBrowserShowStatus.Shown, shown.Status);
        Assert.False(fallback.Used, "The AI Mode page did not accept the question; the Lens fallback answered.");
        Assert.Equal([1], chips.AttachedChips);
        Assert.True(answer.Answered, "AI Mode did not show an answer naming the square color.");
        Assert.InRange(submittedAfter, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        if (Environment.GetEnvironmentVariable("CTS_WEBVIEW2_PREVIEW") == "1")
            await Task.Delay(TimeSpan.FromSeconds(30));
    }

    [SkippableFact]
    [Trait("Category", "Live")]
    public async Task Google_lens_prewarms_hidden_and_logs_results_timing_against_a_cold_browser()
    {
        TestSwitches.Require("CTS_WEBVIEW2_LIVE");

        var paths = new AppPaths(Path.Combine(TestOutputPaths.TempDirectory, "CircleFlow.WebView2Live"));
        AppDataDirectory.Initialize(paths);
        var environments = new WebViewEnvironmentFactory(paths, TestUiStrings.English, new TestPluginNotifier());
        var log = new PluginLog(paths.LogsDirectory);
        var descriptor = new SearchProviderDescriptor(SearchProviderIds.GoogleLens, "Google Lens");
        SearchBrowserHost CreateHost() => new(
            new AppPaths().ExtensionArchivePath,
            new AppPaths().CosmeticFiltersPath,
            paths.SearchProfileDirectory,
            log,
            new StaDispatcher(CompositionRoot.SearchBrowserThreadName),
            () => environments.CreateAsync(paths.SearchProfileDirectory, enableExtensions: true),
            (content, anchor, lightTheme) => CompositionRoot.CreateSearchBrowserWindowView(
                TestUiStrings.English, content, anchor, lightTheme));

        TimeSpan cold;
        using (var host = CreateHost())
        {
            var started = DateTime.UtcNow;
            var shown = await host.ShowAsync(
                descriptor,
                PreparedVisualSearch.ForBrowserOperation(
                    new GoogleLensBrowserOperation(CreateJpeg(), log), externalFallbackUrl: null),
                CancellationToken.None);
            cold = DateTime.UtcNow - started;
            Assert.Equal(SearchBrowserShowStatus.Shown, shown.Status);
        }

        using (var host = CreateHost())
        {
            var image = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var overlayClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var show = host.ShowAsync(
                descriptor,
                PreparedVisualSearch.ForBrowserOperation(
                    new GoogleLensBrowserOperation(image.Task, log), externalFallbackUrl: null, overlayClosed.Task),
                CancellationToken.None);
            // The browser starts while the lasso is drawn and uploads while the overlay closes.
            await Task.Delay(TimeSpan.FromSeconds(2));
            var selectedAt = DateTime.UtcNow;
            image.SetResult(CreateJpeg());
            await Task.Delay(TimeSpan.FromMilliseconds(600));
            overlayClosed.SetResult();
            var shown = await show;
            var warm = DateTime.UtcNow - selectedAt;

            log.Info(
                nameof(GoogleLensLiveSearchTests),
                $"Lens results after the selection: cold {cold.TotalMilliseconds:F0} ms, prewarmed {warm.TotalMilliseconds:F0} ms");
            // Network timing varies too much to assert on, so the comparison is only logged.
            Assert.Equal(SearchBrowserShowStatus.Shown, shown.Status);
            if (Environment.GetEnvironmentVariable("CTS_WEBVIEW2_PREVIEW") == "1")
                await Task.Delay(TimeSpan.FromSeconds(30));
        }
    }

    private sealed class AttachmentCountingOperation(IVisualSearchBrowserOperation inner) : IVisualSearchBrowserOperation
    {
        public List<int> AttachedChips { get; } = [];

        public Task<VisualSearchBrowserOperationStatus> ExecuteAsync(
            IVisualSearchBrowserSession session,
            CancellationToken cancel)
            => inner.ExecuteAsync(new CountingSession(session, AttachedChips), cancel);

        private sealed class CountingSession(IVisualSearchBrowserSession inner, List<int> counts)
            : IVisualSearchBrowserSession
        {
            public Uri? CurrentUri => inner.CurrentUri;

            public void SetDisplayedProvider(SearchProviderDescriptor provider) => inner.SetDisplayedProvider(provider);

            public Task<BrowserNavigationResult> NavigateAsync(Uri target, TimeSpan timeout, CancellationToken cancel)
                => inner.NavigateAsync(target, timeout, cancel);

            public Task<BrowserNavigationResult> NavigatePostAsync(
                Uri target, Stream body, string headers, TimeSpan timeout, CancellationToken cancel)
                => inner.NavigatePostAsync(target, body, headers, timeout, cancel);

            public Task<BrowserNavigationResult> WaitForNavigationAsync(TimeSpan timeout, CancellationToken cancel)
                => inner.WaitForNavigationAsync(timeout, cancel);

            public Task<string> ExecuteScriptAsync(string script, CancellationToken cancel)
                => inner.ExecuteScriptAsync(script, cancel);

            public async Task<string?> PostWebMessageAndWaitAsync(
                string message, Func<string, bool> predicate, TimeSpan timeout, CancellationToken cancel)
            {
                var reply = await inner.PostWebMessageAndWaitAsync(message, predicate, timeout, cancel);
                if (reply?.Contains(":attached", StringComparison.Ordinal) == true)
                {
                    // A late duplicate would appear shortly after the acknowledged chip.
                    await Task.Delay(1500, cancel);
                    var name = JsonDocument.Parse(message).RootElement.GetProperty("name").GetString();
                    var countScript = $"[...document.querySelectorAll('[title=\"{name}\"]')].filter(e => e.offsetParent).length";
                    counts.Add(int.Parse(await inner.ExecuteScriptAsync(countScript, cancel)));
                }
                return reply;
            }
        }
    }

    private sealed class FallbackProbe(IVisualSearchBrowserOperation inner) : IVisualSearchBrowserOperation
    {
        public bool Used { get; private set; }

        public Task<VisualSearchBrowserOperationStatus> ExecuteAsync(
            IVisualSearchBrowserSession session,
            CancellationToken cancel)
        {
            Used = true;
            return inner.ExecuteAsync(session, cancel);
        }
    }

    private sealed class AnswerProbe(IVisualSearchBrowserOperation inner, params string[] answers)
        : IVisualSearchBrowserOperation
    {
        private readonly string[] _answers = answers.Length == 0 ? ["blue", "син"] : answers;

        public Uri? FinalUri { get; private set; }
        public bool Answered { get; private set; }
        public DateTime SubmittedAt { get; private set; }

        public async Task<VisualSearchBrowserOperationStatus> ExecuteAsync(
            IVisualSearchBrowserSession session,
            CancellationToken cancel)
        {
            var status = await inner.ExecuteAsync(session, cancel);
            SubmittedAt = DateTime.UtcNow;
            FinalUri = session.CurrentUri;
            for (var attempt = 0; attempt < 60 && status == VisualSearchBrowserOperationStatus.Succeeded; attempt++)
            {
                var text = System.Text.Json.JsonSerializer.Deserialize<string>(
                    await session.ExecuteScriptAsync("document.body?.innerText ?? ''", cancel)) ?? "";
                if (_answers.Any(expected => text.Contains(expected, StringComparison.OrdinalIgnoreCase)))
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
