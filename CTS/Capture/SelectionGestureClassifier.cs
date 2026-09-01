namespace CircleToSearch.Capture;

using System.Drawing;

internal enum SelectionGestureKind
{
    PixelPick,
    TooSmall,
    VisualSelection,
}

internal static class SelectionGestureClassifier
{
    internal static SelectionGestureKind Classify(
        IReadOnlyList<Point> path,
        int minDiagonalPx,
        int pixelPickMaxDiagonalPx)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count == 0) return SelectionGestureKind.TooSmall;

        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        foreach (var point in path)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        var width = (double)maxX - minX;
        var height = (double)maxY - minY;
        var diagonal = Math.Sqrt(width * width + height * height);
        if (diagonal <= pixelPickMaxDiagonalPx) return SelectionGestureKind.PixelPick;
        return diagonal < minDiagonalPx
            ? SelectionGestureKind.TooSmall
            : SelectionGestureKind.VisualSelection;
    }
}
