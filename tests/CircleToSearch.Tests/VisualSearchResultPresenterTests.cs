using CircleToSearch.Search;
using CircleToSearch.Search.Browser;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class VisualSearchResultPresenterTests
{
    [Fact]
    public async Task Successful_embedded_url_show_never_opens_external_browser()
    {
        var harness = new Harness(SearchBrowserShowStatus.Shown);

        await harness.PresentAsync(harness.UrlPreparation());

        Assert.Equal(1, harness.Host.Calls);
        Assert.Empty(harness.Opened);
        Assert.Empty(harness.Notifier.Errors);
    }

    [Theory]
    [InlineData(SearchBrowserShowStatus.RuntimeUnavailable)]
    [InlineData(SearchBrowserShowStatus.InitializationFailed)]
    [InlineData(SearchBrowserShowStatus.NavigationFailed)]
    public async Task Technical_failure_opens_exact_authorized_fallback_once(
        SearchBrowserShowStatus status)
    {
        var harness = new Harness(status);

        await harness.PresentAsync(harness.UrlPreparation());

        Assert.Equal(["https://yandex.ru/images/search?rpt=imageview"], harness.Opened);
        Assert.Empty(harness.Notifier.Errors);
    }

    [Theory]
    [InlineData(SearchBrowserShowStatus.RuntimeUnavailable)]
    [InlineData(SearchBrowserShowStatus.InitializationFailed)]
    [InlineData(SearchBrowserShowStatus.NavigationFailed)]
    [InlineData(SearchBrowserShowStatus.ProviderOperationFailed)]
    public async Task Failures_without_fallback_notify_once_without_opening(
        SearchBrowserShowStatus status)
    {
        var harness = new Harness(status);

        await harness.PresentAsync(harness.OperationPreparation());

        Assert.Empty(harness.Opened);
        Assert.Single(harness.Notifier.Errors);
    }

    [Fact]
    public async Task Provider_operation_failure_never_uses_even_an_explicit_fallback()
    {
        var harness = new Harness(SearchBrowserShowStatus.ProviderOperationFailed);

        await harness.PresentAsync(harness.UrlPreparation());

        Assert.Empty(harness.Opened);
        Assert.Single(harness.Notifier.Errors);
    }

    [Fact]
    public async Task Canceled_show_is_silent_and_never_opens_fallback()
    {
        var harness = new Harness(SearchBrowserShowStatus.Canceled);

        await harness.PresentAsync(harness.UrlPreparation());

        Assert.Empty(harness.Opened);
        Assert.Empty(harness.Notifier.Errors);
    }

    [Fact]
    public async Task Cancellation_before_show_is_silent()
    {
        var harness = new Harness(SearchBrowserShowStatus.NavigationFailed);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await harness.PresentAsync(harness.UrlPreparation(), cancellation.Token);

        Assert.Equal(0, harness.Host.Calls);
        Assert.Empty(harness.Opened);
        Assert.Empty(harness.Notifier.Errors);
    }

    [Fact]
    public async Task Cancellation_after_failed_show_prevents_external_fallback()
    {
        using var cancellation = new CancellationTokenSource();
        var harness = new Harness(SearchBrowserShowStatus.NavigationFailed)
        {
            OnShow = cancellation.Cancel,
        };

        await harness.PresentAsync(harness.UrlPreparation(), cancellation.Token);

        Assert.Empty(harness.Opened);
        Assert.Empty(harness.Notifier.Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task External_opener_failure_produces_one_error_without_retry(bool throws)
    {
        var harness = new Harness(SearchBrowserShowStatus.NavigationFailed)
        {
            OpenThrows = throws,
            OpenResult = false,
        };

        await harness.PresentAsync(harness.UrlPreparation());

        Assert.Equal(1, harness.OpenCalls);
        Assert.Single(harness.Notifier.Errors);
    }

    [Fact]
    public async Task Generic_presenter_accepts_third_url_and_operation_providers()
    {
        var harness = new Harness(SearchBrowserShowStatus.Shown);
        var url = Routed(
            "third-url",
            PreparedVisualSearch.ForUrl(new Uri("https://example.net/results"), null));
        var operation = Routed(
            "third-operation",
            PreparedVisualSearch.ForBrowserOperation(new FakeOperation(), null));

        await harness.PresentAsync(url);
        await harness.PresentAsync(operation);

        Assert.Equal(2, harness.Host.Calls);
        Assert.Equal([PreparedVisualSearchKind.Url, PreparedVisualSearchKind.BrowserOperation], harness.Host.Kinds);
    }

    private static RoutedVisualSearchPreparation Routed(string id, PreparedVisualSearch prepared) =>
        new(
            id,
            "Third Provider",
            VisualSearchPreparationOutcome.Ready(prepared),
            UsedFallback: false);

    private sealed class Harness
    {
        private readonly VisualSearchResultPresenter _presenter;

        public Harness(SearchBrowserShowStatus status)
        {
            Host = new FakeHost { Status = status, Owner = this };
            var directory = Path.Combine(Path.GetTempPath(), "CircleToSearch.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            _presenter = new VisualSearchResultPresenter(
                Host,
                Open,
                Notifier,
                TestUiStrings.English,
                new PluginLog(directory));
        }

        public FakeHost Host { get; }
        public TestPluginNotifier Notifier { get; } = new();
        public List<string> Opened { get; } = [];
        public int OpenCalls { get; private set; }
        public bool OpenResult { get; set; } = true;
        public bool OpenThrows { get; set; }
        public Action? OnShow { get; set; }

        public RoutedVisualSearchPreparation UrlPreparation()
        {
            var url = new Uri("https://yandex.ru/images/search?rpt=imageview");
            return Routed("test-url", PreparedVisualSearch.ForUrl(url, url));
        }

        public RoutedVisualSearchPreparation OperationPreparation()
            => Routed(
                "test-operation",
                PreparedVisualSearch.ForBrowserOperation(new FakeOperation(), null));

        public Task PresentAsync(
            RoutedVisualSearchPreparation preparation,
            CancellationToken cancel = default)
            => _presenter.PresentAsync(preparation, cancel);

        private bool Open(string url)
        {
            OpenCalls++;
            if (OpenThrows) throw new InvalidOperationException("open failed");
            if (OpenResult) Opened.Add(url);
            return OpenResult;
        }
    }

    private sealed class FakeHost : ISearchBrowserHost
    {
        public required Harness Owner { get; init; }
        public SearchBrowserShowStatus Status { get; init; }
        public int Calls { get; private set; }
        public List<PreparedVisualSearchKind> Kinds { get; } = [];

        public Task<SearchBrowserShowResult> ShowAsync(
            SearchProviderDescriptor descriptor,
            PreparedVisualSearch preparedSearch,
            CancellationToken cancel)
        {
            Calls++;
            Kinds.Add(preparedSearch.Kind);
            Owner.OnShow?.Invoke();
            return Task.FromResult(new SearchBrowserShowResult(Status));
        }
    }

    private sealed class FakeOperation : IVisualSearchBrowserOperation
    {
        public Task<VisualSearchBrowserOperationStatus> ExecuteAsync(
            IVisualSearchBrowserSession session,
            CancellationToken cancel)
            => Task.FromResult(VisualSearchBrowserOperationStatus.Succeeded);
    }
}
