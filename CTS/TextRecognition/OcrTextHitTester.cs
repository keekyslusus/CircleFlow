using System.Drawing;

namespace CircleToSearch.TextRecognition;

public sealed class OcrTextHitTester(double tolerancePx = 3)
{
    public double TolerancePx { get; } = tolerancePx >= 0
        ? tolerancePx
        : throw new ArgumentOutOfRangeException(nameof(tolerancePx));

    public OcrWord? HitTest(OcrDocument? document, Point point)
    {
        if (document is null) return null;
        OcrWord? best = null;
        var bestDistance = double.PositiveInfinity;
        var bestArea = long.MaxValue;
        foreach (var word in document.Words)
        {
            var distance = Distance(word.BoundsPx, point);
            if (distance > TolerancePx) continue;
            var area = (long)word.BoundsPx.Width * word.BoundsPx.Height;
            if (distance > bestDistance ||
                distance == bestDistance && area > bestArea ||
                distance == bestDistance && area == bestArea && best is not null && word.ReadingOrder >= best.ReadingOrder)
                continue;
            best = word;
            bestDistance = distance;
            bestArea = area;
        }
        return best;
    }

    private static double Distance(Rectangle rectangle, Point point)
    {
        var dx = Math.Max(Math.Max(rectangle.Left - point.X, 0), point.X - rectangle.Right);
        var dy = Math.Max(Math.Max(rectangle.Top - point.Y, 0), point.Y - rectangle.Bottom);
        return Math.Sqrt((double)dx * dx + (double)dy * dy);
    }
}
