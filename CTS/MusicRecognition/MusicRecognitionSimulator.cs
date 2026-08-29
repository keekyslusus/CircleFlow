using CircleToSearch.MusicRecognition.Audio;
using CircleToSearch.MusicRecognition.Shazam;
using CircleToSearch.Ui;

namespace CircleToSearch.MusicRecognition;

public enum MusicDebugScenario
{
    Live,
    Matched,
    NoMatch,
    NoAudio,
    DeviceError,
    ServiceError,
    RateLimited,
}

public interface IMusicRecognitionSimulator
{
    Task<MusicRecognitionOutcome> RecognizeAsync(
        MusicDebugScenario scenario,
        IMusicVisualizationProgress? progress,
        CancellationToken cancellationToken);
}

public sealed class MusicRecognitionSimulator : IMusicRecognitionSimulator
{
    private readonly UiStrings _strings;
    private readonly TimeSpan _duration;
    private readonly TimeSpan _frameInterval;

    public MusicRecognitionSimulator(
        UiStrings strings,
        TimeSpan? duration = null,
        TimeSpan? frameInterval = null)
    {
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        _duration = duration ?? TimeSpan.FromMilliseconds(1800);
        _frameInterval = frameInterval ?? TimeSpan.FromMilliseconds(75);
        if (_duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        if (_frameInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(frameInterval));
    }

    public async Task<MusicRecognitionOutcome> RecognizeAsync(
        MusicDebugScenario scenario,
        IMusicVisualizationProgress? progress,
        CancellationToken cancellationToken)
    {
        if (scenario == MusicDebugScenario.Live)
            throw new ArgumentException("The live scenario must use the real recognizer.", nameof(scenario));

        try
        {
            var elapsed = TimeSpan.Zero;
            var frame = 0;
            while (elapsed < _duration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(CreateFrame(scenario, elapsed, frame));
                var remaining = _duration - elapsed;
                var delay = remaining < _frameInterval ? remaining : _frameInterval;
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                elapsed += delay;
                frame++;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return MusicRecognitionOutcome.From(MusicRecognitionStatus.Canceled);
        }

        return scenario switch
        {
            MusicDebugScenario.Matched => MusicRecognitionOutcome.Matched(new ShazamRecognition(
                _strings.DebugMusicTrackTitle,
                _strings.DebugMusicTrackArtist,
                _strings.DebugMusicTrackAlbum,
                _strings.DebugMusicTrackGenre,
                "debug-track",
                null,
                "https://www.shazam.com/track/debug-track")),
            MusicDebugScenario.NoMatch => MusicRecognitionOutcome.From(MusicRecognitionStatus.NoMatch),
            MusicDebugScenario.NoAudio => MusicRecognitionOutcome.From(MusicRecognitionStatus.NoAudio),
            MusicDebugScenario.DeviceError => MusicRecognitionOutcome.From(MusicRecognitionStatus.DeviceError),
            MusicDebugScenario.ServiceError => MusicRecognitionOutcome.From(MusicRecognitionStatus.ServiceError),
            MusicDebugScenario.RateLimited => MusicRecognitionOutcome.From(MusicRecognitionStatus.RateLimited),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
    }

    private static MusicVisualizationFrame CreateFrame(
        MusicDebugScenario scenario,
        TimeSpan elapsed,
        int frame)
    {
        if (scenario is MusicDebugScenario.NoAudio or MusicDebugScenario.DeviceError)
            return new MusicVisualizationFrame(elapsed, 0, 0, false);

        var phase = frame * 0.72;
        var level = 0.28 + Math.Abs(Math.Sin(phase)) * 0.46;
        var transient = frame > 0 && frame % 6 == 0;
        var peak = Math.Min(1, level + (transient ? 0.24 : 0.1));
        return new MusicVisualizationFrame(elapsed, level, peak, transient);
    }
}
