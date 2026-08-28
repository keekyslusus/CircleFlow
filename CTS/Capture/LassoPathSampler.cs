namespace CircleToSearch.Capture;

using System.Drawing;

// Physical pixels in, physical pixels out. Points closer than the threshold to the last
// accepted point are dropped so a drag cannot grow thousands of geometry nodes; the final
// mouse-up point bypasses the threshold so bounds never miss the release position.
public sealed class LassoPathSampler
{
    private readonly double _minDistancePx;
    private readonly List<Point> _points = [];

    public LassoPathSampler(double minDistancePx) => _minDistancePx = Math.Max(0, minDistancePx);

    public IReadOnlyList<Point> Points => _points;

    public bool Add(Point point)
    {
        if (_points.Count > 0 && DistanceToLast(point) < _minDistancePx) return false;
        _points.Add(point);
        return true;
    }

    public bool AddFinal(Point point)
    {
        if (_points.Count > 0 && _points[^1] == point) return false;
        _points.Add(point);
        return true;
    }

    public void Reset() => _points.Clear();

    private double DistanceToLast(Point point)
    {
        var last = _points[^1];
        var dx = (double)point.X - last.X;
        var dy = (double)point.Y - last.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
