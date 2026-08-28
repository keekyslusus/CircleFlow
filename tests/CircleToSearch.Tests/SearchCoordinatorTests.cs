using System.Drawing;
using CircleToSearch.Capture;
using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Shazam;
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
        var gate = new TaskCompletionSource<OverlayOutcome?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var harness = new CoordinatorHarness(
            SearchProviderIds.GoogleLens,
            _ => gate.Task);

        var session = harness.Coordinator.StartFromHotkeyAsync();
        Assert.True(WaitForState(harness.Coordinator, SearchState.Selecting));
        harness.Settings.SearchProviderId = SearchProviderIds.YandexImages;
        gate.SetResult(NewVisualOutcome());
        await session;

        Assert.Equal(1, harness.Google.Calls);
        Assert.Equal(0, harness.Yandex.Calls);
    }

    [Fact]
    public async Task Repeated_hotkey_during_selection_cancels_without_creating_a_provider()
    {
        var gate = new TaskCompletionSource<OverlayOutcome?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Func<CancellationToken, Task<OverlayOutcome?>> selection = cancel =>
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
            selection: _ => Task.FromResult<OverlayOutcome?>(null));

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
            selection: _ => Task.FromResult<OverlayOutcome?>(
                OverlayOutcome.VisualSelection(new SelectionOutcome(new Rectangle(0, 0, 2, 2), bitmap))));
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

        await harness.Coordinator.CancelActiveSession();

        Assert.Equal(SearchState.Idle, harness.Coordinator.State);
    }

    [Theory]
    [InlineData(MusicRecognitionStatus.NoMatch, false, "matching track")]
    [InlineData(MusicRecognitionStatus.NoAudio, false, "default Windows output")]
    [InlineData(MusicRecognitionStatus.RateLimited, true, "rate-limited")]
    [InlineData(MusicRecognitionStatus.ServiceError, true, "could not be reached")]
    [InlineData(MusicRecognitionStatus.DeviceError, true, "could not be captured")]
    public async Task Music_outcomes_map_to_the_expected_flow_message(
        MusicRecognitionStatus status,
        bool error,
        string expected)
    {
        using var harness = new CoordinatorHarness(selection: _ =>
            Task.FromResult<OverlayOutcome?>(OverlayOutcome.MusicRecognition()));
        harness.Music.Outcome = MusicRecognitionOutcome.From(status);

        await harness.Coordinator.StartFromHotkeyAsync();

        var messages = error ? harness.Errors : harness.Messages;
        Assert.Contains(expected, Assert.Single(messages));
        Assert.Equal(0, harness.GoogleFactoryCalls);
        Assert.Equal(1, harness.Music.Calls);
    }

    [Fact]
    public async Task Match_with_safe_Shazam_url_shows_button_and_opens_on_action()
    {
        using var harness = new CoordinatorHarness(selection: _ =>
            Task.FromResult<OverlayOutcome?>(OverlayOutcome.MusicRecognition()));
        harness.Music.Outcome = MusicRecognitionOutcome.Matched(new ShazamRecognition(
            "Track", "Artist", "Album", "Rock", null, null, "https://www.shazam.com/track/1"));

        await harness.Coordinator.StartFromHotkeyAsync();

        var button = Assert.Single(harness.Buttons);
        Assert.Equal("Artist — Track", button.Title);
        Assert.Contains("Album", button.Message);
        button.Action();
        Assert.Equal(["https://www.shazam.com/track/1"], harness.Opened);
    }

    [Theory]
    [InlineData("http://www.shazam.com/track/1")]
    [InlineData("https://evil.example/track/1")]
    [InlineData("https://shazam.com.evil.example/track/1")]
    [InlineData("not a url")]
    public async Task Match_with_unsafe_url_still_shows_result_without_button(string url)
    {
        using var harness = new CoordinatorHarness(selection: _ =>
            Task.FromResult<OverlayOutcome?>(OverlayOutcome.MusicRecognition()));
        harness.Music.Outcome = MusicRecognitionOutcome.Matched(new ShazamRecognition(
            "Track", "Artist", null, null, null, null, url));

        await harness.Coordinator.StartFromHotkeyAsync();

        Assert.Empty(harness.Buttons);
        Assert.Single(harness.Messages);
    }

    [Fact]
    public async Task Repeated_hotkey_during_recognition_cancels_without_a_late_message()
    {
        using var harness = new CoordinatorHarness(selection: _ =>
            Task.FromResult<OverlayOutcome?>(OverlayOutcome.MusicRecognition()));
        harness.Music.Gate = new TaskCompletionSource<MusicRecognitionOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var session = harness.Coordinator.StartFromHotkeyAsync();
        Assert.True(WaitForState(harness.Coordinator, SearchState.RecognizingMusic));
        await harness.Coordinator.StartFromHotkeyAsync();
        await session;

        Assert.Equal(SearchState.Idle, harness.Coordinator.State);
        Assert.Empty(harness.Messages);
        Assert.Empty(harness.Errors);
        Assert.Empty(harness.Buttons);
    }

    private static SelectionOutcome NewSelection()
        => new(new Rectangle(0, 0, 2, 2), new GdiBitmap(2, 2));

    private static OverlayOutcome NewVisualOutcome() => OverlayOutcome.VisualSelection(NewSelection());

    private static bool WaitForState(SearchCoordinator coordinator, SearchState state)
        => SpinWait.SpinUntil(() => coordinator.State == state, TimeSpan.FromSeconds(5));

    private sealed class CoordinatorHarness : IDisposable
    {
        private readonly VisualSearchProviderRouter _router;

        public CoordinatorHarness(
            string providerId = SearchProviderIds.GoogleLens,
            Func<CancellationToken, Task<OverlayOutcome?>>? selection = null,
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
                selection ?? (_ => Task.FromResult<OverlayOutcome?>(NewVisualOutcome())),
                (_, _) => [1, 2, 3],
                Music,
                openUrl ?? (url =>
                {
                    Opened.Add(url);
                    return true;
                }),
                () => Hidden++,
                (_, message) => Messages.Add(message),
                (title, message, button, action) => Buttons.Add((title, message, button, action)),
                (_, message) => Errors.Add(message),
                Settings,
                TestUiStrings.English,
                log);
        }

        public SearchCoordinator Coordinator { get; }

        public PluginSettings Settings { get; }

        public FakeProvider Google { get; } = new();

        public FakeProvider Yandex { get; } = new();

        public FakeMusicRecognizer Music { get; } = new();

        public List<string> Opened { get; } = [];

        public List<string> Errors { get; } = [];

        public List<string> Messages { get; } = [];

        public List<(string Title, string Message, string Button, Action Action)> Buttons { get; } = [];

        public int Hidden { get; private set; }

        public int GoogleFactoryCalls { get; private set; }

        public int YandexFactoryCalls { get; private set; }

        public void Dispose() => _router.Dispose();
    }

    private sealed class FakeMusicRecognizer : IMusicRecognizer
    {
        public int Calls { get; private set; }
        public MusicRecognitionOutcome Outcome { get; set; } =
            MusicRecognitionOutcome.From(MusicRecognitionStatus.NoMatch);
        public TaskCompletionSource<MusicRecognitionOutcome>? Gate { get; set; }

        public Task<MusicRecognitionOutcome> RecognizeAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (Gate is null) return Task.FromResult(Outcome);
            cancellationToken.Register(() => Gate.TrySetResult(
                MusicRecognitionOutcome.From(MusicRecognitionStatus.Canceled)));
            return Gate.Task;
        }
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
