using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Audio;
using CircleToSearch.Search;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class MusicRecognitionWorkflowTests
{
    [Fact]
    public async Task Live_scenario_uses_real_recognizer_with_progress()
    {
        var harness = new Harness();
        var progress = new FakeProgress();

        var outcome = await harness.Workflow.RecognizeAsync(
            MusicDebugScenario.Live, progress, CancellationToken.None);

        Assert.Same(harness.Recognizer.Outcome, outcome);
        Assert.Same(progress, harness.Recognizer.Progress);
        Assert.Equal(1, harness.Recognizer.Calls);
        Assert.Equal(0, harness.Simulator.Calls);
    }

    [Theory]
    [InlineData(MusicDebugScenario.Matched)]
    [InlineData(MusicDebugScenario.NoMatch)]
    [InlineData(MusicDebugScenario.NoAudio)]
    [InlineData(MusicDebugScenario.DeviceError)]
    [InlineData(MusicDebugScenario.ServiceError)]
    [InlineData(MusicDebugScenario.RateLimited)]
    public async Task Debug_scenario_uses_simulator(MusicDebugScenario scenario)
    {
        var harness = new Harness();
        var progress = new FakeProgress();

        var outcome = await harness.Workflow.RecognizeAsync(scenario, progress, CancellationToken.None);

        Assert.Same(harness.Simulator.Outcome, outcome);
        Assert.Equal(scenario, harness.Simulator.Scenario);
        Assert.Same(progress, harness.Simulator.Progress);
        Assert.Equal(0, harness.Recognizer.Calls);
    }

    [Fact]
    public async Task Cancellation_exception_is_not_converted_to_a_ui_outcome()
    {
        var harness = new Harness();
        harness.Recognizer.Exception = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(() => harness.Workflow.RecognizeAsync(
            MusicDebugScenario.Live, null, new CancellationToken(canceled: true)));
    }

    private sealed class Harness
    {
        public Harness()
        {
            var path = Path.Combine(Path.GetTempPath(), "CircleToSearch.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            Workflow = new MusicRecognitionWorkflow(Recognizer, Simulator, new PluginLog(path));
        }

        public FakeRecognizer Recognizer { get; } = new();
        public FakeSimulator Simulator { get; } = new();
        public MusicRecognitionWorkflow Workflow { get; }
    }

    private sealed class FakeRecognizer : IMusicRecognizer
    {
        public int Calls { get; private set; }
        public IMusicVisualizationProgress? Progress { get; private set; }
        public Exception? Exception { get; set; }
        public MusicRecognitionOutcome Outcome { get; } = MusicRecognitionOutcome.From(MusicRecognitionStatus.NoMatch);

        public Task<MusicRecognitionOutcome> RecognizeAsync(CancellationToken cancellationToken) =>
            RecognizeAsync(null, cancellationToken);

        public Task<MusicRecognitionOutcome> RecognizeAsync(
            IMusicVisualizationProgress? progress,
            CancellationToken cancellationToken)
        {
            Calls++;
            Progress = progress;
            return Exception is null
                ? Task.FromResult(Outcome)
                : Task.FromException<MusicRecognitionOutcome>(Exception);
        }
    }

    private sealed class FakeSimulator : IMusicRecognitionSimulator
    {
        public int Calls { get; private set; }
        public MusicDebugScenario? Scenario { get; private set; }
        public IMusicVisualizationProgress? Progress { get; private set; }
        public MusicRecognitionOutcome Outcome { get; } = MusicRecognitionOutcome.From(MusicRecognitionStatus.NoAudio);

        public Task<MusicRecognitionOutcome> RecognizeAsync(
            MusicDebugScenario scenario,
            IMusicVisualizationProgress? progress,
            CancellationToken cancellationToken)
        {
            Calls++;
            Scenario = scenario;
            Progress = progress;
            return Task.FromResult(Outcome);
        }
    }

    private sealed class FakeProgress : IMusicVisualizationProgress
    {
        public void Report(MusicVisualizationFrame frame) { }
    }
}
