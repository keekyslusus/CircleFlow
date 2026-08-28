using System.Drawing;
using CircleToSearch.Capture;
using CircleToSearch.Search;
using CircleToSearch.Settings;
using GdiBitmap = System.Drawing.Bitmap;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class SearchCoordinatorTests
{
    [Fact]
    public async Task Yandex_flow_selects_uploads_and_opens_results_once()
    {
        using var harness = new CoordinatorHarness(SearchProviderIds.YandexImages);
        harness.Yandex.Outcome = VisualSearchOutcome.Ok("https://yandex.ru/images/search?result=1");

        await harness.Coordinator.StartFromHotkeyAsync();

        Assert.Equal(SearchState.Idle, harness.Coordinator.State);
        Assert.Equal(["https://yandex.ru/images/search?result=1"], harness.Opened);
        Assert.Equal(0, harness.Google.Calls);
        Assert.Equal(1, harness.Yandex.Calls);
        Assert.Equal(1, harness.Hidden);
    }

    [Fact]
    public async Task Google_handled_result_does_not_open_an_external_url()
    {
        using var harness = new CoordinatorHarness();
        harness.Google.Outcome = VisualSearchOutcome.Handled();

        await harness.Coordinator.StartFromHotkeyAsync();

        Assert.Empty(harness.Opened);
        Assert.Equal(1, harness.Google.Calls);
        Assert.Equal(0, harness.Yandex.Calls);
    }

    [Fact]
    public async Task Unknown_setting_falls_back_to_google_without_changing_the_setting()
    {
        using var harness = new CoordinatorHarness("missing-provider");

        await harness.Coordinator.StartFromHotkeyAsync();

        Assert.Equal(1, harness.Google.Calls);
        Assert.Equal(0, harness.Yandex.Calls);
        Assert.Equal("missing-provider", harness.Settings.SearchProviderId);
    }

    [Fact]
    public async Task One_session_uses_a_snapshot_of_the_provider_setting()
    {
        var gate = new TaskCompletionSource<SelectionOutcome?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var harness = new CoordinatorHarness(
            SearchProviderIds.GoogleLens,
            _ => gate.Task);

        var session = harness.Coordinator.StartFromHotkeyAsync();
        Assert.True(WaitForState(harness.Coordinator, SearchState.Selecting));
        harness.Settings.SearchProviderId = SearchProviderIds.YandexImages;
        gate.SetResult(NewSelection());
        await session;

        Assert.Equal(1, harness.Google.Calls);
        Assert.Equal(0, harness.Yandex.Calls);
    }

    [Fact]
    public async Task Repeated_hotkey_during_selection_cancels_without_creating_a_provider()
    {
        var gate = new TaskCompletionSource<SelectionOutcome?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Func<CancellationToken, Task<SelectionOutcome?>> selection = cancel =>
        {
            cancel.Register(() => gate.TrySetResult(null));
            return gate.Task;
        };
        using var harness = new CoordinatorHarness(selection: selection);

        var session = harness.Coordinator.StartFromHotkeyAsync();
        Assert.True(WaitForState(harness.Coordinator, SearchState.Selecting));
        await harness.Coordinator.StartFromHotkeyAsync();
        await session;

        Assert.Equal(SearchState.Idle, harness.Coordinator.State);
        Assert.Equal(0, harness.GoogleFactoryCalls);
        Assert.Equal(0, harness.YandexFactoryCalls);
    }

    [Fact]
    public async Task Hotkey_during_upload_is_ignored()
    {
        using var harness = new CoordinatorHarness();
        harness.Google.Gate = new TaskCompletionSource<VisualSearchOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var session = harness.Coordinator.StartFromHotkeyAsync();
        Assert.True(WaitForState(harness.Coordinator, SearchState.Uploading));
        await harness.Coordinator.StartFromHotkeyAsync();
        harness.Google.Gate.SetResult(VisualSearchOutcome.Handled());
        await session;

        Assert.Equal(SearchState.Idle, harness.Coordinator.State);
        Assert.Equal(1, harness.Google.Calls);
    }

    [Fact]
    public async Task Canceled_selection_returns_to_idle_without_creating_a_provider()
    {
        using var harness = new CoordinatorHarness(
            selection: _ => Task.FromResult<SelectionOutcome?>(null));

        await harness.Coordinator.StartFromHotkeyAsync();

        Assert.Equal(SearchState.Idle, harness.Coordinator.State);
        Assert.Equal(0, harness.GoogleFactoryCalls);
        Assert.Equal(0, harness.YandexFactoryCalls);
    }

    [Theory]
    [InlineData(SearchProviderIds.GoogleLens, UploadFailure.BrowserRuntimeUnavailable, "Google Lens", "WebView2")]
    [InlineData(SearchProviderIds.YandexImages, UploadFailure.BrowserAutomationFailed, "Yandex Images", "attached")]
    public async Task Browser_failure_names_the_effective_provider(
        string providerId,
        UploadFailure failure,
        string providerName,
        string expectedText)
    {
        using var harness = new CoordinatorHarness(providerId);
        var provider = providerId == SearchProviderIds.GoogleLens
            ? harness.Google
            : harness.Yandex;
        provider.Outcome = VisualSearchOutcome.Fail(failure);

        await harness.Coordinator.StartFromHotkeyAsync();

        var error = Assert.Single(harness.Errors);
        Assert.Contains(providerName, error);
        Assert.Contains(expectedText, error);
    }

    [Fact]
    public async Task Generic_upload_failure_preserves_existing_error_mapping()
    {
        using var harness = new CoordinatorHarness(SearchProviderIds.YandexImages);
        harness.Yandex.Outcome = VisualSearchOutcome.Fail(UploadFailure.Timeout);

        await harness.Coordinator.StartFromHotkeyAsync();

        Assert.Contains("timed out", Assert.Single(harness.Errors));
        Assert.Empty(harness.Opened);
    }

    [Fact]
    public async Task Browser_open_failure_surfaces_an_error()
    {
        using var harness = new CoordinatorHarness(openUrl: _ => false);
        harness.Google.Outcome = VisualSearchOutcome.Ok("https://example.com/results");

        await harness.Coordinator.StartFromQueryAsync();

        Assert.Single(harness.Errors);
    }

    [Fact]
    public async Task Bitmap_is_disposed_when_the_provider_fails()
    {
        var bitmap = new GdiBitmap(2, 2);
        using var harness = new CoordinatorHarness(
            selection: _ => Task.FromResult<SelectionOutcome?>(
                new SelectionOutcome(new Rectangle(0, 0, 2, 2), bitmap)));
        harness.Google.Exception = new InvalidOperationException("provider exploded");

        await harness.Coordinator.StartFromHotkeyAsync();

        Assert.Throws<ArgumentException>(() => bitmap.GetPixel(0, 0));
        Assert.Single(harness.Errors);
    }

    [Fact]
    public async Task Selection_failure_is_contained()
    {
        using var harness = new CoordinatorHarness(
            selection: _ => throw new InvalidOperationException("capture exploded"));

        await harness.Coordinator.StartFromHotkeyAsync();

        Assert.Equal(SearchState.Idle, harness.Coordinator.State);
        Assert.Equal(0, harness.GoogleFactoryCalls);
        Assert.Single(harness.Errors);
    }

    [Fact]
    public async Task Cancel_while_idle_is_a_noop()
    {
        using var harness = new CoordinatorHarness();

        await harness.Coordinator.CancelActiveSelection();

        Assert.Equal(SearchState.Idle, harness.Coordinator.State);
    }

    private static SelectionOutcome NewSelection()
        => new(new Rectangle(0, 0, 2, 2), new GdiBitmap(2, 2));

    private static bool WaitForState(SearchCoordinator coordinator, SearchState state)
        => SpinWait.SpinUntil(() => coordinator.State == state, TimeSpan.FromSeconds(5));

    private sealed class CoordinatorHarness : IDisposable
    {
        private readonly VisualSearchProviderRouter _router;

        public CoordinatorHarness(
            string providerId = SearchProviderIds.GoogleLens,
            Func<CancellationToken, Task<SelectionOutcome?>>? selection = null,
            Func<string, bool>? openUrl = null)
        {
            var logDirectory = Path.Combine(
                Path.GetTempPath(),
                "CircleToSearch.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(logDirectory);
            var log = new PluginLog(logDirectory);
            Settings = new PluginSettings
            {
                SearchProviderId = providerId,
                HideDelayMilliseconds = 0,
            };
            _router = new VisualSearchProviderRouter(
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
                log);
            Coordinator = new SearchCoordinator(
                _router,
                selection ?? (_ => Task.FromResult<SelectionOutcome?>(NewSelection())),
                (_, _) => [1, 2, 3],
                openUrl ?? (url =>
                {
                    Opened.Add(url);
                    return true;
                }),
                () => Hidden++,
                (_, message) => Errors.Add(message),
                Settings,
                TestUiStrings.English,
                log);
        }

        public SearchCoordinator Coordinator { get; }

        public PluginSettings Settings { get; }

        public FakeProvider Google { get; } = new();

        public FakeProvider Yandex { get; } = new();

        public List<string> Opened { get; } = [];

        public List<string> Errors { get; } = [];

        public int Hidden { get; private set; }

        public int GoogleFactoryCalls { get; private set; }

        public int YandexFactoryCalls { get; private set; }

        public void Dispose() => _router.Dispose();
    }

    private sealed class FakeProvider : IVisualSearchProvider
    {
        public int Calls { get; private set; }

        public VisualSearchOutcome Outcome { get; set; } = VisualSearchOutcome.Handled();

        public Exception? Exception { get; set; }

        public TaskCompletionSource<VisualSearchOutcome>? Gate { get; set; }

        public Task<VisualSearchOutcome> SearchAsync(byte[] png, CancellationToken cancel)
        {
            Calls++;
            if (Exception is not null) return Task.FromException<VisualSearchOutcome>(Exception);
            return Gate?.Task ?? Task.FromResult(Outcome);
        }
    }
}
