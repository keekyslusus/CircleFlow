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

public sealed class ProviderMusicOverlayCoordinatorTests
{
    [Fact]
    public async Task Provider_change_applies_to_current_visual_command_and_persists_once()
    {
        using var harness = new Harness();
        harness.Overlay.Enqueue(new ProviderSelected(SearchProviderIds.YandexImages));
        harness.Overlay.Enqueue(new VisualSelection(NewSelection(), SearchProviderIds.YandexImages));

        await harness.Coordinator.StartFromHotkeyAsync();

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

        await harness.Coordinator.StartFromHotkeyAsync();

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

        await harness.Coordinator.StartFromHotkeyAsync();

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

        await harness.Coordinator.StartFromHotkeyAsync();

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

        await harness.Coordinator.StartFromHotkeyAsync();

        Assert.Equal(SearchProviderIds.GoogleLens, harness.Factory.Options!.InitialProviderId);
        Assert.Equal(0, harness.Google.Calls);
        Assert.Equal(0, harness.Yandex.Calls);
    }

    [Fact]
    public async Task Session_can_open_again_after_a_chip_action_cancels_the_previous_overlay()
    {
        using var harness = new Harness();
        harness.Overlay.Enqueue(new CancelSession());

        await harness.Coordinator.StartFromHotkeyAsync();

        Assert.Equal(SearchState.Idle, harness.Coordinator.State);
        harness.Overlay.Enqueue(new VisualSelection(NewSelection(), SearchProviderIds.GoogleLens));

        await harness.Coordinator.StartFromHotkeyAsync();

        Assert.Equal(SearchState.Idle, harness.Coordinator.State);
        Assert.Equal(1, harness.Google.Calls);
    }

    private static SelectionOutcome NewSelection() =>
        new(new Rectangle(0, 0, 2, 2), new Bitmap(2, 2));

    private sealed class Harness : IDisposable
    {
        private readonly VisualSearchProviderRouter _router;
        private readonly string _logDirectory;

        public Harness(string providerId = SearchProviderIds.GoogleLens, bool saveThrows = false)
        {
            _logDirectory = Path.Combine(Path.GetTempPath(), "CircleToSearch.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_logDirectory);
            var log = new PluginLog(_logDirectory);
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
                log);
            Factory = new FakeOverlayFactory(Overlay);
            Coordinator = new SearchCoordinator(
                _router,
                Factory,
                (_, _) => [1],
                Music,
                Simulator,
                _ => true,
                () => { },
                (_, _) => { },
                (_, _, _, _) => { },
                (_, message) => Errors.Add(message),
                () =>
                {
                    SaveCalls++;
                    if (saveThrows) throw new IOException("disk unavailable");
                },
                Settings,
                TestUiStrings.English,
                log);
        }

        public SearchCoordinator Coordinator { get; }
        public PluginSettings Settings { get; }
        public FakeOverlay Overlay { get; } = new();
        public FakeOverlayFactory Factory { get; }
        public FakeProvider Google { get; } = new();
        public FakeProvider Yandex { get; } = new();
        public FakeMusicRecognizer Music { get; } = new();
        public FakeMusicSimulator Simulator { get; } = new();
        public List<string> Errors { get; } = [];
        public int SaveCalls { get; private set; }

        public void Dispose() => _router.Dispose();
    }

    private sealed class FakeOverlayFactory(FakeOverlay overlay) : IOverlaySessionFactory
    {
        public OverlayLaunchOptions? Options { get; private set; }
        public Task<IOverlaySession?> OpenAsync(OverlayLaunchOptions options, CancellationToken cancellationToken)
        {
            Options = options;
            return Task.FromResult<IOverlaySession?>(overlay);
        }
    }

    private sealed class FakeOverlay : IOverlaySession
    {
        private readonly Channel<IOverlayCommand> _commands = Channel.CreateUnbounded<IOverlayCommand>();
        public void Enqueue(IOverlayCommand command) => _commands.Writer.TryWrite(command);
        public MusicRecognitionOutcome? Result { get; private set; }
        public bool CloseAfterResult { get; set; }
        public Task<IOverlayCommand> ReadCommandAsync(CancellationToken cancellationToken) =>
            _commands.Reader.ReadAsync(cancellationToken).AsTask();
        public Task ShowListeningAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ReportAudioAsync(MusicVisualizationFrame frame, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ShowMusicResultAsync(MusicRecognitionOutcome outcome, CancellationToken cancellationToken)
        {
            Result = outcome;
            if (CloseAfterResult) Enqueue(new CancelSession());
            return Task.CompletedTask;
        }
        public Task CloseAsync() => Task.CompletedTask;
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

        public Task<MusicRecognitionOutcome> RecognizeAsync(CancellationToken cancellationToken)
        {
            Calls++;
            if (Gate is null) return Task.FromResult(MusicRecognitionOutcome.From(MusicRecognitionStatus.NoMatch));
            cancellationToken.Register(() =>
            {
                CanceledCalls++;
                Gate.TrySetResult(MusicRecognitionOutcome.From(MusicRecognitionStatus.Canceled));
            });
            return Gate.Task;
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
