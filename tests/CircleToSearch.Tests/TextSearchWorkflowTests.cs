using CircleToSearch.Search;
using CircleToSearch.Search.Browser;
using CircleToSearch.Shell;
using CircleToSearch.Ui;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class TextSearchWorkflowTests
{
    [Fact]
    public async Task Opens_validated_url_and_reports_opener_failure()
    {
        var harness = new Harness(open: _ => false);

        await harness.ExecuteAsync("a&b", SearchProviderIds.GoogleLens);

        Assert.Contains("q=a%26b", Assert.Single(harness.Opened));
        Assert.Equal([TestUiStrings.English.TextSearchOpenFailed], harness.Errors);
        Assert.Equal(0, harness.Host.Shows);
    }

    [Fact]
    public async Task Uses_the_engine_selected_at_execution_time()
    {
        var harness = new Harness();

        await harness.ExecuteAsync("cat", SearchProviderIds.YandexImages);
        harness.Engine = "duckduckgo";
        await harness.ExecuteAsync("cat", SearchProviderIds.YandexImages);

        Assert.Equal(["https://yandex.com/search/?text=cat", "https://duckduckgo.com/?q=cat"], harness.Opened);
    }

    [Fact]
    public async Task Oversized_query_is_rejected_before_browser_open()
    {
        var harness = new Harness(maximumScalars: 2) { BuiltInBrowser = true };

        await harness.ExecuteAsync("abc", SearchProviderIds.GoogleLens);

        Assert.Empty(harness.Opened);
        Assert.Equal(0, harness.Host.Shows);
        Assert.Equal(0, harness.BrowserStartedCalls);
        Assert.Equal([TestUiStrings.English.TextSearchTooLong], harness.Errors);
        Assert.Null(harness.Workflow.TryCreateResultsUrl("abc", SearchProviderIds.GoogleLens));
        Assert.Single(harness.Errors);
    }

    [Fact]
    public async Task Built_in_browser_shows_the_results_named_after_their_site()
    {
        var harness = new Harness { BuiltInBrowser = true };

        await harness.ExecuteAsync("a&b", SearchProviderIds.GoogleLens);

        Assert.Equal(["google.com"], harness.Host.Names);
        Assert.Equal([new Uri("https://www.google.com/search?q=a%26b")], harness.Host.Navigated);
        Assert.Empty(harness.Host.Scripts);
        Assert.Equal(1, harness.BrowserStartedCalls);
        Assert.Empty(harness.Opened);
        Assert.Empty(harness.Errors);
    }

    [Fact]
    public async Task Built_in_browser_failure_opens_the_results_in_the_default_browser()
    {
        var unavailable = new Harness { BuiltInBrowser = true };
        unavailable.Host.ShowFailure = SearchBrowserShowStatus.RuntimeUnavailable;
        var failedNavigation = new Harness { BuiltInBrowser = true };
        failedNavigation.Host.Navigation = BrowserNavigationResult.Failed();

        await unavailable.ExecuteAsync("cat", SearchProviderIds.YandexImages);
        await failedNavigation.ExecuteAsync("cat", SearchProviderIds.YandexImages);

        Assert.Equal(["https://yandex.com/search/?text=cat"], unavailable.Opened);
        Assert.Equal(["https://yandex.com/search/?text=cat"], failedNavigation.Opened);
        Assert.Empty(unavailable.Errors);
        Assert.Empty(failedNavigation.Errors);
    }

    [Fact]
    public async Task Warm_browser_navigates_once_results_arrive_and_falls_back_only_after_its_reveal()
    {
        var harness = new Harness { Engine = "bing" };
        harness.Host.Navigation = BrowserNavigationResult.TimedOut();
        var results = new TaskCompletionSource<Uri>();
        var reveal = new TaskCompletionSource();

        var warming = harness.Workflow.WarmAsync(SearchProviderIds.TraceMoe, results.Task, reveal.Task, CancellationToken.None)!;
        Assert.Equal(["bing.com"], harness.Host.Names);
        Assert.Empty(harness.Host.Navigated);
        Assert.Contains("'preconnect'", Assert.Single(harness.Host.Scripts));
        Assert.Contains("\"https://www.bing.com/\"", harness.Host.Scripts[0]);
        results.SetResult(harness.Workflow.TryCreateResultsUrl("cat", SearchProviderIds.TraceMoe)!);
        await Task.Delay(20);
        Assert.False(warming.IsCompleted);
        Assert.Empty(harness.Opened);
        reveal.SetResult();
        await warming;

        Assert.Equal([new Uri("https://www.bing.com/search?q=cat")], harness.Host.Navigated);
        Assert.Equal(["https://www.bing.com/search?q=cat"], harness.Opened);
    }

    [Fact]
    public async Task Canceled_warm_browser_opens_nothing()
    {
        var harness = new Harness();
        using var cancellation = new CancellationTokenSource();

        var warming = harness.Workflow.WarmAsync(SearchProviderIds.GoogleLens, new TaskCompletionSource<Uri>().Task,
            new TaskCompletionSource().Task, cancellation.Token)!;
        cancellation.Cancel();
        await warming;

        Assert.Empty(harness.Host.Navigated);
        Assert.Empty(harness.Opened);
        Assert.Null(harness.Workflow.WarmAsync("unknown", Task.FromResult(new Uri("https://example.com")),
            Task.CompletedTask, CancellationToken.None));
    }

    private sealed class Harness
    {
        public Harness(Func<string, bool>? open = null, int maximumScalars = 2000)
        {
            var logDirectory = Path.Combine(TestOutputPaths.TempDirectory, "text-search-logs");
            Directory.CreateDirectory(logDirectory);
            var notifier = new Notifier(Errors);
            var log = new PluginLog(logDirectory);
            Workflow = new TextSearchWorkflow(
                new TextSearchUrlBuilder(maximumScalars),
                () => Engine,
                () => BuiltInBrowser,
                Host,
                new UrlOpeningService(url =>
                {
                    Opened.Add(url);
                    return open?.Invoke(url) ?? true;
                }, notifier, TestUiStrings.English, log),
                notifier,
                TestUiStrings.English,
                log);
        }

        public TextSearchWorkflow Workflow { get; }
        public string Engine { get; set; } = TextSearchEngines.MatchImageSearch;
        public bool BuiltInBrowser { get; init; }
        public NavigatingBrowserHost Host { get; } = new();
        public List<string> Opened { get; } = [];
        public List<string> Errors { get; } = [];
        public int BrowserStartedCalls { get; private set; }

        public Task ExecuteAsync(string text, string providerId) =>
            Workflow.ExecuteAsync(text, providerId, () => BrowserStartedCalls++, CancellationToken.None);
    }

    private sealed class Notifier(List<string> errors) : IPluginNotifier
    {
        public void ShowMessage(string title, string message) { }
        public void ShowMessageWithButton(string title, string message, string button, Action action) { }
        public void ShowError(string title, string message) => errors.Add(message);
    }
}
