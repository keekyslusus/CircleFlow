namespace CircleToSearch.Capture;

internal enum ToastTone
{
    Neutral,
    Error,
    Success,
}

internal readonly record struct ToastColorSample(byte Red, byte Green, byte Blue)
{
    internal string HexCode => FormattableString.Invariant($"#{Red:X2}{Green:X2}{Blue:X2}");
}

internal sealed record ToastNotification
{
    internal static readonly TimeSpan DefaultDuration = TimeSpan.FromMilliseconds(3000);

    internal ToastNotification(string message, ToastTone tone)
        : this(message, tone, DefaultDuration, null)
    {
    }

    internal ToastNotification(string message, ToastTone tone, TimeSpan duration)
        : this(message, tone, duration, null)
    {
    }

    internal ToastNotification(string message, ToastTone tone, ToastColorSample colorSample)
        : this(message, tone, DefaultDuration, colorSample)
    {
    }

    internal ToastNotification(
        string message,
        ToastTone tone,
        TimeSpan duration,
        ToastColorSample? colorSample)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("A toast message is required.", nameof(message));
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), "A toast duration must be positive.");

        Message = message;
        Tone = tone;
        Duration = duration;
        ColorSample = colorSample;
    }

    internal string Message { get; }
    internal ToastTone Tone { get; }
    internal TimeSpan Duration { get; }
    internal ToastColorSample? ColorSample { get; }

    internal string AccessibleMessage => ColorSample is { } sample
        ? $"{Message} {sample.HexCode}"
        : Message;
}
