using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Audio;

namespace CircleToSearch.Search;

internal sealed class MusicRecognitionWorkflow(
    IMusicRecognizer recognizer,
    IMusicRecognitionSimulator simulator,
    PluginLog log)
{
    public Task<MusicRecognitionOutcome> RecognizeAsync(
        MusicDebugScenario scenario,
        IMusicVisualizationProgress? progress,
        CancellationToken cancellationToken)
    {
        log.Info(nameof(MusicRecognitionWorkflow), scenario == MusicDebugScenario.Live
            ? "music recognition started"
            : $"simulated music recognition started with '{scenario}'");
        return scenario == MusicDebugScenario.Live
            ? recognizer.RecognizeAsync(progress, cancellationToken)
            : simulator.RecognizeAsync(scenario, progress, cancellationToken);
    }
}
