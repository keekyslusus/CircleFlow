using System.Windows;
using GdiPoint = System.Drawing.Point;
using GdiRectangle = System.Drawing.Rectangle;
using GdiSize = System.Drawing.Size;

namespace CircleToSearch.Capture;

public sealed class OverlayCoordinateMapper
{
    private readonly double _scale;
    private readonly double _overscanInsetDips;
    private readonly GdiSize _captureSize;

    public OverlayCoordinateMapper(double scale, bool overscan, GdiSize captureSize)
    {
        if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
        if (captureSize.Width <= 0 || captureSize.Height <= 0) throw new ArgumentOutOfRangeException(nameof(captureSize));
        _scale = scale;
        _overscanInsetDips = overscan ? 1 : 0;
        _captureSize = captureSize;
    }

    public GdiPoint ToPhysical(Point dip, bool clamp = true)
    {
        var x = (int)Math.Round((dip.X - _overscanInsetDips) * _scale);
        var y = (int)Math.Round((dip.Y - _overscanInsetDips) * _scale);
        return !clamp
            ? new GdiPoint(x, y)
            : new GdiPoint(Math.Clamp(x, 0, _captureSize.Width - 1), Math.Clamp(y, 0, _captureSize.Height - 1));
    }

    public Point ToDips(GdiPoint point) => new(
        point.X / _scale + _overscanInsetDips,
        point.Y / _scale + _overscanInsetDips);

    public Rect ToDips(GdiRectangle rectangle) => new(
        rectangle.X / _scale + _overscanInsetDips,
        rectangle.Y / _scale + _overscanInsetDips,
        rectangle.Width / _scale,
        rectangle.Height / _scale);

    public GdiRectangle CaptureBounds => new(0, 0, _captureSize.Width, _captureSize.Height);
}
