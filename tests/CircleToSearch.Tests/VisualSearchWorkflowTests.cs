using System.Drawing;
using CircleToSearch.Capture;
using CircleToSearch.Search;
using CircleToSearch.Search.Browser;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class VisualSearchWorkflowTests
{
    [Fact]
    public async Task Successful_preparation_disposes_bitmap_transitions_once_and_presents_result()
    {
        using var harness = new Harness();
        var bitmap = new Bitmap(2, 2);
        var transitions = 0;

        await harness.Workflow.ExecuteAsync(
            new SelectionOutcome(new Rectangle(0, 0, 2, 2), bitmap),
            SearchProviderIds.GoogleLens,
            () => transitions++,
            CancellationToken.None);

        Assert.Equal(1, transitions);
        Assert.Equal(1, harness.Host.Calls);
        Assert.Throws<ArgumentException>(() => bitmap.GetPixel(0, 0));
        Assert.Empty(harness.Notifier.Errors);
    }

    [Fact]
    public async Task Yandex_selection_routes_once_to_the_shared_host()
    {
        using var harness = new Harness();

        await harness.Workflow.ExecuteAsync(
            new SelectionOutcome(new Rectangle(0, 0, 2, 2), new Bitmap(2, 2)),
            SearchProviderIds.YandexImages,
            () => { },
            CancellationToken.None);

        Assert.Equal(0, harness.Google.Calls);
        Assert.Equal(1, harness.Yandex.Calls);
        Assert.Equal(1, harness.Host.Calls);
        Assert.Equal(SearchProviderIds.YandexImages, harness.Host.LastDescriptor!.Id);
    }

    [Fact]
    public async Task Yandex_navigation_failure_does_not_repeat_preparation_and_opens_fallback_once()
    {
        using var harness = new Harness();
        var results = new Uri("https://yandex.ru/images/search?rpt=imageview");
        harness.Yandex.Outcome = VisualSearchPreparationOutcome.Ready(
            PreparedVisualSearch.ForUrl(results, results));
        harness.Host.Status = SearchBrowserShowStatus.NavigationFailed;

        await harness.Workflow.ExecuteAsync(
            new SelectionOutcome(new Rectangle(0, 0, 2, 2), new Bitmap(2, 2)),
            SearchProviderIds.YandexImages,
            () => { },
            CancellationToken.None);

        Assert.Equal(1, harness.Yandex.Calls);
        Assert.Equal(1, harness.Host.Calls);
        Assert.Equal([results.AbsoluteUri], harness.Opened);
    }

    [Fact]
    public async Task Unknown_provider_routes_to_default()
    {
        using var harness = new Harness();

        await harness.Workflow.ExecuteAsync(
            new SelectionOutcome(new Rectangle(0, 0, 2, 2), new Bitmap(2, 2)),
            "missing",
            () => { },
            CancellationToken.None);

        Assert.Equal(1, harness.Google.Calls);
        Assert.Equal(0, harness.Yandex.Calls);
    }

    [Fact]
    public async Task Upload_callback_runs_once_before_provider_invocation()
    {
        using var harness = new Harness();
        var events = new List<string>();
        harness.Google.OnCall = () => events.Add("provider");

        await harness.Workflow.ExecuteAsync(
            new SelectionOutcome(new Rectangle(0, 0, 2, 2), new Bitmap(2, 2)),
            SearchProviderIds.GoogleLens,
            () => events.Add("upload"),
            CancellationToken.None);

        Assert.Equal(["upload", "provider"], events);
    }

    [Fact]
    public async Task Crop_exception_disposes_bitmap_without_transitioning_or_notifying()
    {
        using var harness = new Harness(crop: (_, _) => throw new InvalidOperationException("crop failed"));
        var bitmap = new Bitmap(2, 2);
        var transitions = 0;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Workflow.ExecuteAsync(
            new SelectionOutcome(new Rectangle(0, 0, 2, 2), bitmap),
            SearchProviderIds.GoogleLens,
            () => transitions++,
            CancellationToken.None));

        Assert.Equal("crop failed", exception.Message);
        Assert.Equal(0, transitions);
        Assert.Throws<ArgumentException>(() => bitmap.GetPixel(0, 0));
        Assert.Empty(harness.Notifier.Errors);
    }

    [Fact]
    public async Task Provider_exception_propagates_after_bitmap_is_disposed()
    {
        using var harness = new Harness();
        harness.Google.Exception = new InvalidOperationException("provider failed");
        var bitmap = new Bitmap(2, 2);

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Workflow.ExecuteAsync(
            new SelectionOutcome(new Rectangle(0, 0, 2, 2), bitmap),
            SearchProviderIds.GoogleLens,
            () => { },
            CancellationToken.None));

        Assert.Throws<ArgumentException>(() => bitmap.GetPixel(0, 0));
        Assert.Empty(harness.Notifier.Errors);
    }

    [Theory]
    [InlineData(UploadFailure.UnexpectedStatus, "503")]
    [InlineData(UploadFailure.BadResponse, "unexpected response")]
    [InlineData(UploadFailure.PolicyRejection, "unexpected")]
    [InlineData(UploadFailure.Timeout, "timed out")]
    [InlineData(UploadFailure.NetworkError, "network error")]
    public async Task Preparation_failure_maps_to_existing_message(UploadFailure failure, string expected)
    {
        using var harness = new Harness();
        harness.Google.Outcome = VisualSearchPreparationOutcome.Fail(failure, 503);

        await harness.ExecuteAsync();

        Assert.Contains(expected, Assert.Single(harness.Notifier.Errors).Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, harness.Host.Calls);
    }

    [Fact]
    public async Task Canceled_preparation_does_not_notify_or_show_browser()
    {
        using var harness = new Harness();
        harness.Google.Outcome = VisualSearchPreparationOutcome.Fail(UploadFailure.Canceled);

        await harness.ExecuteAsync();

        Assert.Empty(harness.Notifier.Errors);
        Assert.Equal(0, harness.Host.Calls);
    }

    private sealed class Harness : IDisposable
    {
        private readonly VisualSearchProviderRouter _router;

        public Harness(Func<Bitmap, Rectangle, byte[]>? crop = null)
        {
            var log = NewLog();
            _router = new VisualSearchProviderRouter(
                [
                    new VisualSearchProviderRegistration(
                        new SearchProviderDescriptor(SearchProviderIds.GoogleLens, "Google Lens"),
                        () => Google),
                    new VisualSearchProviderRegistration(
                        new SearchProviderDescriptor(SearchProviderIds.YandexImages, "Yandex Images"),
                        () => Yandex),
                ],
                SearchProviderIds.GoogleLens,
                log);
            var presenter = new VisualSearchResultPresenter(
                Host,
                url =>
                {
                    Opened.Add(url);
                    return true;
                },
                Notifier,
                TestUiStrings.English,
                log);
            Workflow = new VisualSearchWorkflow(
                _router,
                crop ?? ((_, _) => [1, 2, 3]),
                presenter,
                Notifier,
                TestUiStrings.English,
                log);
        }

        public VisualSearchWorkflow Workflow { get; }
        public FakeProvider Google { get; } = new();
        public FakeProvider Yandex { get; } = new();
        public FakeHost Host { get; } = new();
        public TestPluginNotifier Notifier { get; } = new();
        public List<string> Opened { get; } = [];

        public Task ExecuteAsync() => Workflow.ExecuteAsync(
            new SelectionOutcome(new Rectangle(0, 0, 2, 2), new Bitmap(2, 2)),
            SearchProviderIds.GoogleLens,
            () => { },
            CancellationToken.None);

        public void Dispose() => _router.Dispose();
    }

    private sealed class FakeProvider : IVisualSearchProvider
    {
        public int Calls { get; private set; }
        public VisualSearchPreparationOutcome Outcome { get; set; } =
            VisualSearchPreparationOutcome.Ready(
                PreparedVisualSearch.ForUrl(new Uri("https://example.com/result"), null));
        public Exception? Exception { get; set; }
        public Action? OnCall { get; set; }

        public Task<VisualSearchPreparationOutcome> PrepareAsync(byte[] png, CancellationToken cancel)
        {
            Calls++;
            OnCall?.Invoke();
            return Exception is null
                ? Task.FromResult(Outcome)
                : Task.FromException<VisualSearchPreparationOutcome>(Exception);
        }
    }

    private sealed class FakeHost : ISearchBrowserHost
    {
        public SearchBrowserShowStatus Status { get; set; } = SearchBrowserShowStatus.Shown;
        public int Calls { get; private set; }
        public SearchProviderDescriptor? LastDescriptor { get; private set; }

        public Task<SearchBrowserShowResult> ShowAsync(
            SearchProviderDescriptor descriptor,
            PreparedVisualSearch preparedSearch,
            CancellationToken cancel)
        {
            Calls++;
            LastDescriptor = descriptor;
            return Task.FromResult(new SearchBrowserShowResult(Status));
        }
    }

    private static PluginLog NewLog()
    {
        var path = Path.Combine(Path.GetTempPath(), "CircleToSearch.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new PluginLog(path);
    }
}
