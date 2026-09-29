namespace CircleToSearch.Capture;

using System.Drawing;

public sealed record OverlayOptions(int PaddingPx, int MinDiagonalPx);

public sealed class SelectionOutcome : IDisposable
{
    private Bitmap? _frozenFrame;

    public SelectionOutcome(Rectangle bounds, Bitmap frozenFrame)
    {
        Bounds = bounds;
        _frozenFrame = frozenFrame;
    }

    public Rectangle Bounds { get; }

    public Bitmap FrozenFrame => Volatile.Read(ref _frozenFrame)
        ?? throw new ObjectDisposedException(nameof(SelectionOutcome));

    // Encoding consumes the selection so its full-screen frame is released as soon as the bytes exist.
    public byte[] Encode(Func<Bitmap, Rectangle, int, byte[]> crop, int maxLongSidePx)
    {
        try { return crop(FrozenFrame, Bounds, maxLongSidePx); }
        finally { Dispose(); }
    }

    public void Dispose() => Interlocked.Exchange(ref _frozenFrame, null)?.Dispose();
}

public enum OverlayAction
{
    VisualSelection,
    MusicRecognition,
}

public sealed record OverlayOutcome
{
    private OverlayOutcome(OverlayAction action, SelectionOutcome? selection)
    {
        if (action == OverlayAction.VisualSelection && selection is null)
            throw new ArgumentException("A visual action requires a selection.", nameof(selection));
        if (action != OverlayAction.VisualSelection && selection is not null)
            throw new ArgumentException("Only a visual action can contain a selection.", nameof(selection));

        Action = action;
        Selection = selection;
    }

    public OverlayAction Action { get; }

    public SelectionOutcome? Selection { get; }

    public static OverlayOutcome VisualSelection(SelectionOutcome selection) =>
        new(OverlayAction.VisualSelection, selection);

    public static OverlayOutcome MusicRecognition() => new(OverlayAction.MusicRecognition, null);
}
