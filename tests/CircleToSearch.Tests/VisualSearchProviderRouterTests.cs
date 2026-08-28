using CircleToSearch.Search;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class VisualSearchProviderRouterTests
{
    [Theory]
    [InlineData(SearchProviderIds.GoogleLens, 1, 0)]
    [InlineData(SearchProviderIds.YandexImages, 0, 1)]
    public async Task Known_id_uses_only_the_selected_provider(
        string requestedId,
        int expectedGoogleCalls,
        int expectedYandexCalls)
    {
        using var harness = new RouterHarness();

        var routed = await harness.Router.SearchAsync(requestedId, [1], CancellationToken.None);

        Assert.Equal(expectedGoogleCalls, harness.Google.Calls);
        Assert.Equal(expectedYandexCalls, harness.Yandex.Calls);
        Assert.False(routed.UsedFallback);
    }

    [Fact]
    public async Task Id_matching_is_case_insensitive_and_returns_the_canonical_id()
    {
        using var harness = new RouterHarness();

        var routed = await harness.Router.SearchAsync("YANDEX-IMAGES", [1], CancellationToken.None);

        Assert.Equal(SearchProviderIds.YandexImages, routed.ProviderId);
        Assert.Equal("Yandex Images", routed.ProviderDisplayName);
        Assert.False(routed.UsedFallback);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("missing-provider")]
    public async Task Invalid_id_uses_the_default_and_marks_fallback(string? requestedId)
    {
        using var harness = new RouterHarness();

        var routed = await harness.Router.SearchAsync(requestedId, [1], CancellationToken.None);

        Assert.Equal(SearchProviderIds.GoogleLens, routed.ProviderId);
        Assert.True(routed.UsedFallback);
        Assert.Equal(1, harness.GoogleFactoryCalls);
        Assert.Equal(0, harness.YandexFactoryCalls);
    }

    [Fact]
    public async Task Unknown_id_logs_a_safe_warning()
    {
        using var harness = new RouterHarness();

        await harness.Router.SearchAsync("missing-provider", [71, 72, 73], CancellationToken.None);

        var log = File.ReadAllText(Path.Combine(harness.LogDirectory, "plugin.log"));
        Assert.Contains("missing-provider", log);
        Assert.Contains(SearchProviderIds.GoogleLens, log);
        Assert.DoesNotContain("71, 72, 73", log);
    }

    [Fact]
    public async Task Repeated_searches_reuse_one_lazy_provider_instance()
    {
        using var harness = new RouterHarness();

        await harness.Router.SearchAsync(SearchProviderIds.YandexImages, [1], CancellationToken.None);
        await harness.Router.SearchAsync(SearchProviderIds.YandexImages, [2], CancellationToken.None);

        Assert.Equal(1, harness.YandexFactoryCalls);
        Assert.Equal(2, harness.Yandex.Calls);
        Assert.Equal(0, harness.GoogleFactoryCalls);
    }

    [Fact]
    public async Task Selected_provider_failure_does_not_invoke_the_default_provider()
    {
        using var harness = new RouterHarness();
        harness.Yandex.Exception = new InvalidOperationException("provider failed");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Router.SearchAsync(SearchProviderIds.YandexImages, [1], CancellationToken.None));

        Assert.Equal("provider failed", exception.Message);
        Assert.Equal(0, harness.GoogleFactoryCalls);
        Assert.Equal(1, harness.YandexFactoryCalls);
    }

    [Fact]
    public async Task Selected_factory_failure_does_not_invoke_the_default_factory()
    {
        var defaultFactoryCalls = 0;
        using var router = new VisualSearchProviderRouter(
            [
                new VisualSearchProviderRegistration(
                    new SearchProviderDescriptor(SearchProviderIds.GoogleLens, "Google Lens"),
                    () =>
                    {
                        defaultFactoryCalls++;
                        return new FakeProvider();
                    }),
                new VisualSearchProviderRegistration(
                    new SearchProviderDescriptor(SearchProviderIds.YandexImages, "Yandex Images"),
                    () => throw new InvalidOperationException("factory failed")),
            ],
            SearchProviderIds.GoogleLens,
            SilentLog());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => router.SearchAsync(SearchProviderIds.YandexImages, [1], CancellationToken.None));

        Assert.Equal("factory failed", exception.Message);
        Assert.Equal(0, defaultFactoryCalls);
    }

    [Fact]
    public async Task Search_passes_the_original_cancellation_token()
    {
        using var harness = new RouterHarness();
        using var cancellation = new CancellationTokenSource();

        await harness.Router.SearchAsync(
            SearchProviderIds.GoogleLens,
            [1],
            cancellation.Token);

        Assert.Equal(cancellation.Token, harness.Google.LastToken);
    }

    [Fact]
    public async Task Dispose_releases_only_created_providers_once_without_forcing_factories()
    {
        var harness = new RouterHarness();
        await harness.Router.SearchAsync(SearchProviderIds.YandexImages, [1], CancellationToken.None);

        harness.Router.Dispose();
        harness.Router.Dispose();

        Assert.Equal(0, harness.GoogleFactoryCalls);
        Assert.Equal(1, harness.YandexFactoryCalls);
        Assert.Equal(0, harness.Google.DisposeCalls);
        Assert.Equal(1, harness.Yandex.DisposeCalls);
    }

    [Fact]
    public async Task Dispose_waits_for_an_active_search_before_releasing_its_provider()
    {
        var harness = new RouterHarness();
        harness.Yandex.Gate = new TaskCompletionSource<VisualSearchOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var search = harness.Router.SearchAsync(
            SearchProviderIds.YandexImages,
            [1],
            CancellationToken.None);
        Assert.Equal(1, harness.Yandex.Calls);

        var disposeStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var disposal = Task.Run(() =>
        {
            disposeStarted.TrySetResult();
            harness.Router.Dispose();
        });
        await disposeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            var firstCompleted = await Task.WhenAny(disposal, Task.Delay(100));
            Assert.NotSame(disposal, firstCompleted);
            Assert.Equal(0, harness.Yandex.DisposeCalls);
        }
        finally
        {
            harness.Yandex.Gate.TrySetResult(VisualSearchOutcome.Handled());
        }

        await search;
        await disposal;
        Assert.Equal(1, harness.Yandex.DisposeCalls);
    }

    private sealed class RouterHarness : IDisposable
    {
        public RouterHarness()
        {
            LogDirectory = Path.Combine(
                Path.GetTempPath(),
                "CircleToSearch.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(LogDirectory);
            Router = new VisualSearchProviderRouter(
                [
                    new VisualSearchProviderRegistration(
                        new SearchProviderDescriptor(SearchProviderIds.GoogleLens, "Google Lens"),
                        () =>
                        {
                            GoogleFactoryCalls++;
                            return Google;
                        }),
                    new VisualSearchProviderRegistration(
                        new SearchProviderDescriptor(SearchProviderIds.YandexImages, "Yandex Images"),
                        () =>
                        {
                            YandexFactoryCalls++;
                            return Yandex;
                        }),
                ],
                SearchProviderIds.GoogleLens,
                new PluginLog(LogDirectory));
        }

        public string LogDirectory { get; }

        public FakeProvider Google { get; } = new();

        public FakeProvider Yandex { get; } = new();

        public int GoogleFactoryCalls { get; private set; }

        public int YandexFactoryCalls { get; private set; }

        public VisualSearchProviderRouter Router { get; }

        public void Dispose() => Router.Dispose();
    }

    private static PluginLog SilentLog()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "CircleToSearch.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return new PluginLog(directory);
    }

    private sealed class FakeProvider : IVisualSearchProvider, IDisposable
    {
        public int Calls { get; private set; }

        public int DisposeCalls { get; private set; }

        public CancellationToken LastToken { get; private set; }

        public Exception? Exception { get; set; }

        public TaskCompletionSource<VisualSearchOutcome>? Gate { get; set; }

        public Task<VisualSearchOutcome> SearchAsync(byte[] png, CancellationToken cancel)
        {
            Calls++;
            LastToken = cancel;
            if (Exception is not null) return Task.FromException<VisualSearchOutcome>(Exception);
            return Gate?.Task ?? Task.FromResult(VisualSearchOutcome.Handled());
        }

        public void Dispose() => DisposeCalls++;
    }
}
