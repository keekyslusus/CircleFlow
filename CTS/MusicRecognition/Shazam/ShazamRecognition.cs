namespace CircleToSearch.MusicRecognition.Shazam;

public sealed record ShazamRecognition(
    string Title,
    string Artist,
    string? Album,
    string? Genre,
    string? TrackKey,
    string? CoverUrl,
    string? ShazamUrl);
