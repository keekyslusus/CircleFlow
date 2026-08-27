namespace CircleToSearch.Capture;

using System.Drawing;

public sealed record OverlayOptions(int PaddingPx, int MinDiagonalPx);

// Bounds are physical monitor pixels; FrozenFrame is owned by the receiver, which must dispose it.
public sealed record SelectionOutcome(Rectangle Bounds, Bitmap FrozenFrame);
