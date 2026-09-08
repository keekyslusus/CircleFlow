namespace CircleToSearch.Capture;

internal enum ToastTone
{
    Neutral,
    Error,
    Success,
}

internal sealed record ToastNotification
{
    internal static readonly TimeSpan DefaultDuration = TimeSpan.FromMilliseconds(3000);

    internal ToastNotification(string message, ToastTone tone)
        : this(message, tone, DefaultDuration)
    {
    }

    internal ToastNotification(
        string message,
        ToastTone tone,
        TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("A toast message is required.", nameof(message));
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), "A toast duration must be positive.");

        Message = message;
        Tone = tone;
        Duration = duration;
    }

    internal string Message { get; }
    internal ToastTone Tone { get; }
    internal TimeSpan Duration { get; }
}
