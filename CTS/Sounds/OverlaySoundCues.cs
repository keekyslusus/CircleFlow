namespace CircleToSearch.Sounds;

internal sealed class OverlaySoundCues(Action<UiSound> play, TimeProvider? time = null) : IDisposable
{
    // The scan for QR codes starts with the entrance, so its cue would otherwise land on top of the entrance cue.
    internal static readonly TimeSpan EntranceGap = TimeSpan.FromMilliseconds(300);
    // Spacing ticks by distance makes a fast stroke rattle and a resting pointer go quiet, like pencil friction.
    internal const double TraceStepDips = 36;
    internal static readonly TimeSpan TraceMinInterval = TimeSpan.FromMilliseconds(30);
    // Three detents per wheel notch: the ticks follow the eased zoom, so they slow down as it settles.
    internal static readonly double ZoomStep = Math.Log(1.25) / 3;
    // Pushing against the maximum repeats with every notch; one bump per push is enough.
    internal static readonly TimeSpan ZoomLimitMinInterval = TimeSpan.FromMilliseconds(180);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private long? _enteredAt;
    private long? _lastTraceAt;
    private double _traced;
    private long? _lastZoomAt;
    private double _zoomed;
    private long? _lastLimitAt;
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

    internal void Zoomed(double logChange)
    {
        // A turn in the other direction starts its own detents instead of finishing the previous one.
        if (Math.Sign(logChange) != Math.Sign(_zoomed)) _zoomed = 0;
        _zoomed += logChange;
        if (Math.Abs(_zoomed) < ZoomStep) return;
        if (_lastZoomAt is { } last && _time.GetElapsedTime(last) < TraceMinInterval) return;
        _zoomed %= ZoomStep;
        _lastZoomAt = _time.GetTimestamp();
        play(logChange > 0 ? UiSound.ZoomIn : UiSound.ZoomOut);
    }

    internal void ZoomLimitReached()
    {
        if (_lastLimitAt is { } last && _time.GetElapsedTime(last) < ZoomLimitMinInterval) return;
        _lastLimitAt = _time.GetTimestamp();
        play(UiSound.ZoomLimit);
    }

    public void Dispose()
    {
        _disposed = true;
        _pending?.Dispose();
    }
}
