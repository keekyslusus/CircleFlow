namespace CircleToSearch.Sounds;

internal sealed class OverlaySoundCues(Action<UiSound> play, TimeProvider? time = null) : IDisposable
{
    // The scan for QR codes starts with the entrance, so its cue would otherwise land on top of the entrance cue.
    internal static readonly TimeSpan EntranceGap = TimeSpan.FromMilliseconds(300);
    // Spacing ticks by distance makes a fast stroke rattle and a resting pointer go quiet, like pencil friction.
    internal const double TraceStepDips = 36;
    internal static readonly TimeSpan TraceMinInterval = TimeSpan.FromMilliseconds(30);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private long? _enteredAt;
    private long? _lastTraceAt;
    private double _traced;
    private ITimer? _pending;
    private bool _disposed;

    internal void Entered()
    {
        _enteredAt = _time.GetTimestamp();
        play(UiSound.OverlayOpened);
    }

    internal void QrFound()
    {
        if (_disposed) return;
        var remaining = _enteredAt is { } enteredAt ? EntranceGap - _time.GetElapsedTime(enteredAt) : TimeSpan.Zero;
        if (remaining <= TimeSpan.Zero)
        {
            play(UiSound.QrFound);
            return;
        }
        _pending?.Dispose();
        _pending = _time.CreateTimer(_ => play(UiSound.QrFound), null, remaining, Timeout.InfiniteTimeSpan);
    }

    internal void Traced(double distanceDips)
    {
        _traced += distanceDips;
        if (_traced < TraceStepDips) return;
        if (_lastTraceAt is { } last && _time.GetElapsedTime(last) < TraceMinInterval) return;
        _traced %= TraceStepDips;
        _lastTraceAt = _time.GetTimestamp();
        play(UiSound.Trace);
    }

    public void Dispose()
    {
        _disposed = true;
        _pending?.Dispose();
    }
}
