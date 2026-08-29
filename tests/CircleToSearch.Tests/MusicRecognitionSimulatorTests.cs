using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Audio;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class MusicRecognitionSimulatorTests
{
    [Theory]
    [InlineData(MusicDebugScenario.NoMatch, MusicRecognitionStatus.NoMatch)]
    [InlineData(MusicDebugScenario.NoAudio, MusicRecognitionStatus.NoAudio)]
    [InlineData(MusicDebugScenario.DeviceError, MusicRecognitionStatus.DeviceError)]
    [InlineData(MusicDebugScenario.ServiceError, MusicRecognitionStatus.ServiceError)]
    [InlineData(MusicDebugScenario.RateLimited, MusicRecognitionStatus.RateLimited)]
    public async Task Scenario_returns_expected_local_outcome(
        MusicDebugScenario scenario,
        MusicRecognitionStatus expected)
    {
        var simulator = new MusicRecognitionSimulator(TestUiStrings.English, TimeSpan.Zero);

        var outcome = await simulator.RecognizeAsync(scenario, null, CancellationToken.None);

        Assert.Equal(expected, outcome.Status);
        Assert.Null(outcome.Recognition);
    }

    [Fact]
    public async Task Matched_scenario_uses_translated_fixture_metadata()
    {
        var simulator = new MusicRecognitionSimulator(TestUiStrings.English, TimeSpan.Zero);

        var outcome = await simulator.RecognizeAsync(
            MusicDebugScenario.Matched,
            null,
            CancellationToken.None);

        Assert.Equal(MusicRecognitionStatus.Matched, outcome.Status);
        Assert.Equal(TestUiStrings.English.DebugMusicTrackTitle, outcome.Recognition?.Title);
        Assert.Equal(TestUiStrings.English.DebugMusicTrackArtist, outcome.Recognition?.Artist);
    }

    [Fact]
    public async Task Simulation_reports_waveform_frames_before_result()
    {
        var progress = new RecordingProgress();
        var simulator = new MusicRecognitionSimulator(
            TestUiStrings.English,
            TimeSpan.FromMilliseconds(15),
            TimeSpan.FromMilliseconds(5));

        var outcome = await simulator.RecognizeAsync(
            MusicDebugScenario.NoMatch,
            progress,
            CancellationToken.None);

        Assert.Equal(MusicRecognitionStatus.NoMatch, outcome.Status);
        Assert.Equal(3, progress.Frames.Count);
        Assert.Contains(progress.Frames, frame => frame.NormalizedLevel > 0);
    }

    [Fact]
    public async Task Canceled_simulation_returns_canceled_without_a_result()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var simulator = new MusicRecognitionSimulator(
            TestUiStrings.English,
            TimeSpan.FromSeconds(1));

        var outcome = await simulator.RecognizeAsync(
            MusicDebugScenario.Matched,
            null,
            cancellation.Token);

        Assert.Equal(MusicRecognitionStatus.Canceled, outcome.Status);
        Assert.Null(outcome.Recognition);
    }

    [Fact]
    public async Task Live_scenario_is_rejected_by_simulator()
    {
        var simulator = new MusicRecognitionSimulator(TestUiStrings.English, TimeSpan.Zero);

        await Assert.ThrowsAsync<ArgumentException>(() => simulator.RecognizeAsync(
            MusicDebugScenario.Live,
            null,
            CancellationToken.None));
    }

    private sealed class RecordingProgress : IMusicVisualizationProgress
    {
        public List<MusicVisualizationFrame> Frames { get; } = [];

        public void Report(MusicVisualizationFrame frame) => Frames.Add(frame);
    }
}
