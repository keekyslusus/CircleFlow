using CircleToSearch.MusicRecognition.Shazam;
using CircleToSearch.MusicRecognition.Audio;

namespace CircleToSearch.MusicRecognition;

public enum MusicRecognitionStatus
{
    Matched,
    NoMatch,
    NoAudio,
    RateLimited,
    ServiceError,
    DeviceError,
    Canceled,
}

public sealed record MusicRecognitionOutcome(MusicRecognitionStatus Status, ShazamRecognition? Recognition = null)
{
    public static MusicRecognitionOutcome Matched(ShazamRecognition recognition) =>
        new(MusicRecognitionStatus.Matched, recognition);

    public static MusicRecognitionOutcome From(MusicRecognitionStatus status) => new(status);
}

public interface IMusicRecognizer
{
    Task<MusicRecognitionOutcome> RecognizeAsync(CancellationToken cancellationToken);

    Task<MusicRecognitionOutcome> RecognizeAsync(
        IMusicVisualizationProgress? progress,
        CancellationToken cancellationToken) => RecognizeAsync(cancellationToken);
}
