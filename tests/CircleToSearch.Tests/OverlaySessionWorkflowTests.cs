using System.Drawing;
using System.Threading.Channels;
using CircleToSearch.Capture;
using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Audio;
using CircleToSearch.MusicRecognition.Shazam;
using CircleToSearch.Search;
using CircleToSearch.Settings;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class OverlaySessionWorkflowTests
{
    [Fact]
    public async Task Color_copied_is_terminal_without_search_music_provider_or_browser_side_effects()
    {
        using var harness = new Harness();
        harness.Overlay.Enqueue(new ColorCopied());

        await harness.RunAsync();

        Assert.Equal(1, harness.Overlay.CloseCalls);
        Assert.Equal(0, harness.Google.Calls);
        Assert.Equal(0, harness.Yandex.Calls);
        Assert.Equal(0, harness.Music.Calls);
        Assert.Equal(0, harness.Simulator.Calls);
        Assert.Equal(0, harness.SaveCalls);
        Assert.Empty(harness.Opened);
    }

    [Fact]
    public async Task Provider_change_applies_to_current_visual_command_and_persists_once()
    {
        using var harness = new Harness();
        harness.Overlay.Enqueue(new ProviderSelected(SearchProviderIds.YandexImages));
        harness.Overlay.Enqueue(new VisualSelection(NewSelection(), SearchProviderIds.YandexImages));

        await harness.RunAsync();

        Assert.Equal(SearchProviderIds.YandexImages, harness.Settings.SearchProviderId);
        Assert.Equal(1, harness.SaveCalls);
        Assert.Equal(0, harness.Google.Calls);
        Assert.Equal(1, harness.Yandex.Calls);
    }

    [Fact]
    public async Task Persistence_failure_keeps_current_provider_and_allows_visual_search()
    {
        using var harness = new Harness(saveThrows: true);
        harness.Overlay.Enqueue(new ProviderSelected(SearchProviderIds.YandexImages));
        harness.Overlay.Enqueue(new VisualSelection(NewSelection(), SearchProviderIds.YandexImages));

        await harness.RunAsync();

        Assert.Equal(SearchProviderIds.YandexImages, harness.Settings.SearchProviderId);
        Assert.Equal(1, harness.Yandex.Calls);
        Assert.Single(harness.Errors);
    }

    [Fact]
    public async Task Provider_change_during_listening_does_not_cancel_recognition()
    {
        using var harness = new Harness();
        harness.Music.Gate = new TaskCompletionSource<MusicRecognitionOutcome>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Overlay.Enqueue(new StartMusicRecognition());
        harness.Overlay.Enqueue(new ProviderSelected(SearchProviderIds.YandexImages));
        harness.Overlay.Enqueue(new CancelSession());

        await harness.RunAsync();

        Assert.Equal(SearchProviderIds.YandexImages, harness.Settings.SearchProviderId);
        Assert.Equal(1, harness.SaveCalls);
        Assert.Equal(1, harness.Music.Calls);
        Assert.Equal(0, harness.Simulator.Calls);
        Assert.Equal(1, harness.Music.CanceledCalls);
    }

    [Fact]
    public async Task Debug_scenario_uses_simulator_without_calling_live_recognizer()
    {
        using var harness = new Harness();
        harness.Overlay.CloseAfterResult = true;
        harness.Overlay.Enqueue(new MusicDebugScenarioSelected(MusicDebugScenario.Matched));
        harness.Overlay.Enqueue(new StartMusicRecognition());

        await harness.RunAsync();

        Assert.Equal(0, harness.Music.Calls);
        Assert.Equal(1, harness.Simulator.Calls);
        Assert.Equal(MusicDebugScenario.Matched, harness.Simulator.LastScenario);
        Assert.Equal(MusicRecognitionStatus.Matched, harness.Overlay.Result?.Status);
    }

    [Fact]
    public async Task Unknown_saved_provider_opens_with_router_default_without_instantiating_providers()
    {
        using var harness = new Harness("missing");
        harness.Overlay.Enqueue(new CancelSession());

        await harness.RunAsync();

        Assert.Equal(SearchProviderIds.GoogleLens, harness.Factory.Options!.InitialProviderId);
        Assert.Equal(0, harness.Google.Calls);
        Assert.Equal(0, harness.Yandex.Calls);
    }

    [Fact]
    public async Task Coordinator_opens_a_fresh_overlay_after_the_previous_session_finishes()
    {
        var first = new FakeOverlay();
        var second = new FakeOverlay();
        first.Enqueue(new CancelSession());
        second.Enqueue(new VisualSelection(NewSelection(), SearchProviderIds.GoogleLens));
        using var harness = new Harness(overlays: [first, second]);
        var coordinator = harness.CreateCoordinator();

        await coordinator.StartFromHotkeyAsync();

        Assert.Equal(SearchState.Idle, coordinator.State);
        Assert.Equal(1, harness.Factory.Calls);
        Assert.Same(first, Assert.Single(harness.Factory.Opened));

        await coordinator.StartFromHotkeyAsync();

        Assert.Equal(SearchState.Idle, coordinator.State);
        Assert.Equal(2, harness.Factory.Calls);
        Assert.Equal([first, second], harness.Factory.Opened);
        Assert.Equal(1, first.CloseCalls);
        Assert.Equal(1, second.CloseCalls);
        Assert.Equal(1, harness.Google.Calls);
    }

    [Fact]
    public async Task Visual_selection_reports_upload_started_exactly_once()
    {
        using var harness = new Harness();
        harness.Overlay.Enqueue(new VisualSelection(NewSelection(), SearchProviderIds.GoogleLens));

        await harness.RunAsync();

        Assert.Equal(1, harness.UploadStartedCalls);
    }

    [Fact]
    public async Task Music_recognition_shows_listening_and_result_without_reporting_upload()
    {
        using var harness = new Harness();
        harness.Overlay.CloseAfterResult = true;
        harness.Overlay.Enqueue(new StartMusicRecognition());

        await harness.RunAsync();

        Assert.Equal(1, harness.Overlay.ListeningCalls);
        Assert.Equal(1, harness.Overlay.ResultCalls);
        Assert.Equal(0, harness.UploadStartedCalls);
    }

    [Fact]
    public async Task Dismissing_music_result_returns_to_selection_in_the_same_overlay()
    {
        using var harness = new Harness();
        harness.Overlay.CommandsAfterResult.Add(new DismissMusicResult());
        harness.Overlay.CommandsAfterResult.Add(
            new VisualSelection(NewSelection(), SearchProviderIds.GoogleLens));
        harness.Overlay.Enqueue(new StartMusicRecognition());

        await harness.RunAsync();

        Assert.Equal(MusicRecognitionStatus.NoMatch, harness.Overlay.Result?.Status);
        Assert.Equal(1, harness.Overlay.ListeningCalls);
        Assert.Equal(1, harness.Overlay.ResultCalls);
        Assert.Equal(1, harness.Google.Calls);
        Assert.Equal(1, harness.UploadStartedCalls);
    }

    [Fact]
    public async Task Closed_command_channel_completes_and_closes_overlay()
    {
        using var harness = new Harness();
        harness.Overlay.CompleteCommands();

        await harness.RunAsync();

        Assert.Equal(1, harness.Overlay.CloseCalls);
    }

    [Fact]
    public async Task Session_cancellation_cancels_recognition_and_closes_overlay()
    {
        using var harness = new Harness();
        harness.Music.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Overlay.Enqueue(new StartMusicRecognition());
        using var cancellation = new CancellationTokenSource();

        var workflow = harness.RunAsync(cancellation.Token);
        Assert.True(SpinWait.SpinUntil(() => harness.Music.Calls == 1, TimeSpan.FromSeconds(5)));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workflow);

        Assert.Equal(1, harness.Music.CanceledCalls);
        Assert.Equal(1, harness.Overlay.CloseCalls);
    }

    [Fact]
    public async Task Coordinator_hotkey_during_recognition_cancels_the_session()
    {
        using var harness = new Harness();
        harness.Music.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Overlay.Enqueue(new StartMusicRecognition());
        var coordinator = harness.CreateCoordinator();

        var session = coordinator.StartFromHotkeyAsync();
        Assert.True(SpinWait.SpinUntil(() => harness.Music.Calls == 1, TimeSpan.FromSeconds(5)));
        Assert.Equal(SearchState.Cancelable, coordinator.State);

        await coordinator.StartFromHotkeyAsync();
        await session;

        Assert.Equal(1, harness.Music.CanceledCalls);
        Assert.Equal(1, harness.Overlay.CloseCalls);
        Assert.Equal(SearchState.Idle, coordinator.State);
    }

    [Fact]
    public async Task Coordinator_hotkey_while_music_result_is_shown_cancels_the_session()
    {
        using var harness = new Harness();
        harness.Overlay.Enqueue(new StartMusicRecognition());
        var coordinator = harness.CreateCoordinator();

        var session = coordinator.StartFromHotkeyAsync();
        Assert.True(SpinWait.SpinUntil(() => harness.Overlay.ResultCalls == 1, TimeSpan.FromSeconds(5)));
        Assert.Equal(SearchState.Cancelable, coordinator.State);

        await coordinator.StartFromHotkeyAsync();
        await session;

        Assert.Equal(1, harness.Overlay.ListeningCalls);
        Assert.Equal(1, harness.Overlay.ResultCalls);
        Assert.Equal(1, harness.Overlay.CloseCalls);
        Assert.Equal(SearchState.Idle, coordinator.State);
    }

    [Fact]
    public async Task Retry_starts_a_new_recognition_after_the_previous_result()
    {
        using var harness = new Harness();
        harness.Overlay.OnResult = count =>
            harness.Overlay.Enqueue(count == 1 ? new RetryMusicRecognition() : new CancelSession());
        harness.Overlay.Enqueue(new StartMusicRecognition());

        await harness.RunAsync();

        Assert.Equal(2, harness.Music.Calls);
        Assert.Equal(2, harness.Overlay.ListeningCalls);
        Assert.Equal(2, harness.Overlay.ResultCalls);
        Assert.Equal(0, harness.UploadStartedCalls);
    }

    [Fact]
    public async Task Overlay_result_failure_uses_fallback_and_closes_workflow()
    {
        using var harness = new Harness();
        harness.Music.Outcome = MusicRecognitionOutcome.From(MusicRecognitionStatus.NoMatch);
        harness.Overlay.ShowResultException = new InvalidOperationException("render failed");
        harness.Overlay.Enqueue(new StartMusicRecognition());

        await harness.RunAsync();

        Assert.Single(harness.Messages);
        Assert.Equal(1, harness.Overlay.CloseCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://example.com/track/1")]
    public async Task Unsafe_or_missing_music_url_keeps_overlay_open(string? url)
    {
        using var harness = new Harness();
        harness.Music.Outcome = MusicRecognitionOutcome.Matched(Match(url));
        harness.Overlay.CommandsAfterResult.Add(new OpenMusicResult());
        harness.Overlay.CommandsAfterResult.Add(new CancelSession());
        harness.Overlay.Enqueue(new StartMusicRecognition());

        await harness.RunAsync();

        Assert.Equal(1, harness.Overlay.CloseCalls);
        Assert.Empty(harness.Opened);
    }

    [Fact]
    public async Task Safe_music_url_closes_overlay_before_opening()
    {
        using var harness = new Harness();
        harness.Music.Outcome = MusicRecognitionOutcome.Matched(Match("https://www.shazam.com/track/1"));
        harness.Overlay.CommandsAfterResult.Add(new OpenMusicResult());
        harness.Overlay.Enqueue(new StartMusicRecognition());

        await harness.RunAsync();

        Assert.True(harness.Events.IndexOf("close") < harness.Events.IndexOf("open"));
        Assert.Single(harness.Opened);
    }

    [Fact]
    public async Task Copy_music_result_has_no_workflow_side_effect()
    {
        using var harness = new Harness();
        harness.Music.Outcome = MusicRecognitionOutcome.Matched(Match("https://www.shazam.com/track/1"));
        harness.Overlay.CommandsAfterResult.Add(new CopyMusicResult());
        harness.Overlay.CommandsAfterResult.Add(new CancelSession());
        harness.Overlay.Enqueue(new StartMusicRecognition());

        await harness.RunAsync();

        Assert.Empty(harness.Opened);
        Assert.Empty(harness.Errors);
    }

    [Fact]
    public async Task Recognition_visualization_is_forwarded_to_overlay()
    {
        using var harness = new Harness();
        harness.Music.ReportFrame = true;
        harness.Overlay.CloseAfterResult = true;
        harness.Overlay.Enqueue(new StartMusicRecognition());

        await harness.RunAsync();

        Assert.Equal(1, harness.Overlay.AudioFrames);
    }

    private static SelectionOutcome NewSelection() =>
        new(new Rectangle(0, 0, 2, 2), new Bitmap(2, 2));

    private static ShazamRecognition Match(string? url) =>
        new("Track", "Artist", null, null, null, null, url);

    private sealed class Harness : IDisposable
    {
        private readonly VisualSearchProviderRouter _router;
        private readonly string _logDirectory;

        public Harness(
            string providerId = SearchProviderIds.GoogleLens,
            bool saveThrows = false,
            IReadOnlyList<FakeOverlay>? overlays = null)
        {
            _logDirectory = Path.Combine(Path.GetTempPath(), "CircleToSearch.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_logDirectory);
            Log = new PluginLog(_logDirectory);
            Settings = new PluginSettings { SearchProviderId = providerId, HideDelayMilliseconds = 0 };
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
                Log);
            var overlaySessions = overlays ?? [new FakeOverlay()];
            Overlay = overlaySessions[0];
            Factory = new FakeOverlayFactory(overlaySessions);
            Notifier = new FakeNotifier(Errors, Messages);
            var visualSearch = new VisualSearchWorkflow(
                _router,
                (_, _) => [1],
                _ => true,
                Notifier,
                TestUiStrings.English,
                Log);
            var musicRecognition = new MusicRecognitionWorkflow(Music, Simulator, Log);
            var musicPresenter = new MusicResultPresenter(
                url => { Events.Add("open"); Opened.Add(url); return true; },
                Notifier,
                TestUiStrings.English);
            var providerSelection = new ProviderSelectionStore(
                _router,
                Settings,
                () =>
                {
                    SaveCalls++;
                    if (saveThrows) throw new IOException("disk unavailable");
                },
                Notifier,
                TestUiStrings.English,
                Log);
            Workflow = new OverlaySessionWorkflow(
                Factory,
                visualSearch,
                musicRecognition,
                musicPresenter,
                providerSelection,
                Settings,
                TestUiStrings.English,
                Log);
        }

        public OverlaySessionWorkflow Workflow { get; }
        public PluginSettings Settings { get; }
        public PluginLog Log { get; }
        public FakeNotifier Notifier { get; }
        public FakeOverlay Overlay { get; }
        public FakeOverlayFactory Factory { get; }
        public FakeProvider Google { get; } = new();
        public FakeProvider Yandex { get; } = new();
        public FakeMusicRecognizer Music { get; } = new();
        public FakeMusicSimulator Simulator { get; } = new();
        public List<string> Errors { get; } = [];
        public List<string> Messages { get; } = [];
        public List<string> Opened { get; } = [];
        public List<string> Events => Overlay.Events;
        public int UploadStartedCalls { get; private set; }
        public int SaveCalls { get; private set; }

        public Task RunAsync(CancellationToken cancellationToken = default) =>
            Workflow.RunAsync(() => UploadStartedCalls++, cancellationToken);

        public SearchCoordinator CreateCoordinator() => new(
            Workflow,
            () => { },
            Settings,
            Notifier,
            TestUiStrings.English,
            Log);

        public void Dispose() => _router.Dispose();
    }

    private sealed class FakeNotifier(List<string> errors, List<string> messages) : CircleToSearch.Ui.IPluginNotifier
    {
        public void ShowMessage(string title, string message) => messages.Add(message);
        public void ShowMessageWithButton(string title, string message, string button, Action action) =>
            messages.Add(message);
        public void ShowError(string title, string message) => errors.Add(message);
    }

    private sealed class FakeOverlayFactory : IOverlaySessionFactory
    {
        private readonly Queue<FakeOverlay> _overlays;

        public FakeOverlayFactory(IEnumerable<FakeOverlay> overlays) => _overlays = new Queue<FakeOverlay>(overlays);

        public OverlayLaunchOptions? Options { get; private set; }
        public int Calls { get; private set; }
        public List<FakeOverlay> Opened { get; } = [];

        public Task<IOverlaySession?> OpenAsync(OverlayLaunchOptions options, CancellationToken cancellationToken)
        {
            Calls++;
            Options = options;
            var overlay = _overlays.Dequeue();
            Opened.Add(overlay);
            return Task.FromResult<IOverlaySession?>(overlay);
        }
    }

    private sealed class FakeOverlay : IOverlaySession
    {
        private readonly Channel<IOverlayCommand> _commands = Channel.CreateUnbounded<IOverlayCommand>();
        public void Enqueue(IOverlayCommand command) => _commands.Writer.TryWrite(command);
        public void CompleteCommands() => _commands.Writer.TryComplete();
        public MusicRecognitionOutcome? Result { get; private set; }
        public bool CloseAfterResult { get; set; }
        public Exception? ShowResultException { get; set; }
        public Action<int>? OnResult { get; set; }
        public int ResultCalls { get; private set; }
        public int ListeningCalls { get; private set; }
        public int CloseCalls { get; private set; }
        public int AudioFrames { get; private set; }
        public List<string> Events { get; } = [];
        public List<IOverlayCommand> CommandsAfterResult { get; } = [];
        public Task<IOverlayCommand> ReadCommandAsync(CancellationToken cancellationToken) =>
            _commands.Reader.ReadAsync(cancellationToken).AsTask();
        public Task ShowListeningAsync(CancellationToken cancellationToken)
        {
            ListeningCalls++;
            Events.Add("listening");
            return Task.CompletedTask;
        }
        public Task ReportAudioAsync(MusicVisualizationFrame frame, CancellationToken cancellationToken)
        {
            AudioFrames++;
            return Task.CompletedTask;
        }
        public Task ShowMusicResultAsync(MusicRecognitionOutcome outcome, CancellationToken cancellationToken)
        {
            if (ShowResultException is not null) throw ShowResultException;
            Result = outcome;
            ResultCalls++;
            Events.Add("result");
            foreach (var command in CommandsAfterResult) Enqueue(command);
            OnResult?.Invoke(ResultCalls);
            if (CloseAfterResult) Enqueue(new CancelSession());
            return Task.CompletedTask;
        }
        public Task CloseAsync()
        {
            CloseCalls++;
            Events.Add("close");
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeProvider : IVisualSearchProvider
    {
        public int Calls { get; private set; }
        public Task<VisualSearchOutcome> SearchAsync(byte[] png, CancellationToken cancel)
        {
            Calls++;
            return Task.FromResult(VisualSearchOutcome.Handled());
        }
    }

    private sealed class FakeMusicRecognizer : IMusicRecognizer
    {
        public int Calls { get; private set; }
        public int CanceledCalls { get; private set; }
        public TaskCompletionSource<MusicRecognitionOutcome>? Gate { get; set; }
        public MusicRecognitionOutcome Outcome { get; set; } =
            MusicRecognitionOutcome.From(MusicRecognitionStatus.NoMatch);
        public bool ReportFrame { get; set; }

        public Task<MusicRecognitionOutcome> RecognizeAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (Gate is null) return Task.FromResult(Outcome);
            cancellationToken.Register(() =>
            {
                CanceledCalls++;
                Gate.TrySetResult(MusicRecognitionOutcome.From(MusicRecognitionStatus.Canceled));
            });
            return Gate.Task;
        }

        public Task<MusicRecognitionOutcome> RecognizeAsync(
            IMusicVisualizationProgress? progress,
            CancellationToken cancellationToken)
        {
            if (ReportFrame)
                progress?.Report(new MusicVisualizationFrame(TimeSpan.Zero, 0.5, 0.7, false));
            return RecognizeAsync(cancellationToken);
        }
    }

    private sealed class FakeMusicSimulator : IMusicRecognitionSimulator
    {
        public int Calls { get; private set; }
        public MusicDebugScenario? LastScenario { get; private set; }

        public Task<MusicRecognitionOutcome> RecognizeAsync(
            MusicDebugScenario scenario,
            IMusicVisualizationProgress? progress,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastScenario = scenario;
            return Task.FromResult(MusicRecognitionOutcome.Matched(new ShazamRecognition(
                "Track", "Artist", null, null, null, null, null)));
        }
    }
}
