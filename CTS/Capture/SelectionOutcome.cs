namespace CircleToSearch.Capture;

using System.Drawing;

public sealed record OverlayOptions(int PaddingPx, int MinDiagonalPx);

// Bounds are physical monitor pixels; FrozenFrame is owned by the receiver, which must dispose it.
public sealed record SelectionOutcome(Rectangle Bounds, Bitmap FrozenFrame);

public enum OverlayAction
{
    VisualSelection,
    MusicRecognition,
    ColorCopied,
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
        new(OverlayAction.VisualSelection, selection ?? throw new ArgumentNullException(nameof(selection)));

    public static OverlayOutcome MusicRecognition() => new(OverlayAction.MusicRecognition, null);

    public static OverlayOutcome ColorCopied() => new(OverlayAction.ColorCopied, null);
}
