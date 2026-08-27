namespace CircleToSearch.Capture;

using System.Drawing;

public static class LassoBoundsCalculator
{
    public static Rectangle? Calculate(
        IReadOnlyList<Point> path,
        Rectangle monitor,
        int paddingPx,
        int minDiagonalPx)
    {
        if (path.Count == 0) return null;

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

        var width = maxX - minX;
        var height = maxY - minY;
        var diagonal = Math.Sqrt((double)width * width + (double)height * height);
        if (diagonal < minDiagonalPx) return null;

        var bounds = Rectangle.FromLTRB(minX, minY, maxX, maxY);
        bounds.Inflate(paddingPx, paddingPx);
        bounds.Intersect(monitor);
        return bounds.IsEmpty ? null : bounds;
    }
}
