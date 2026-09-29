using CircleToSearch.MusicRecognition;
using CircleToSearch.MusicRecognition.Audio;
using CircleToSearch.MusicRecognition.Shazam;

namespace CircleToSearch.Search;

internal sealed class MusicRecognitionWorkflow(
    IMusicRecognizer recognizer,
    IMusicRecognitionSimulator simulator,
    Action<ShazamRecognition> recordMatch,
    PluginLog log)
{
    public async Task<MusicRecognitionOutcome> RecognizeAsync(
        MusicDebugScenario scenario,
        IMusicVisualizationProgress? progress,
        CancellationToken cancellationToken)
    {
        log.Info(nameof(MusicRecognitionWorkflow), scenario == MusicDebugScenario.Live
            ? "music recognition started"
            : $"simulated music recognition started with '{scenario}'");
        if (scenario != MusicDebugScenario.Live)
            return await simulator.RecognizeAsync(scenario, progress, cancellationToken).ConfigureAwait(false);
        var outcome = await recognizer.RecognizeAsync(progress, cancellationToken).ConfigureAwait(false);
        if (outcome is { Status: MusicRecognitionStatus.Matched, Recognition: { } match })
        {
            try { recordMatch(match); }
            catch (Exception exception)
            {
                log.SafeError(nameof(MusicRecognitionWorkflow), "record-history", exception);
            }
        }
        return outcome;
    }
}
