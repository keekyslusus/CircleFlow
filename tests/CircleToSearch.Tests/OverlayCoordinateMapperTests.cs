using System.Windows;
using CircleToSearch.Capture;
using Xunit;
using GdiPoint = System.Drawing.Point;
using GdiSize = System.Drawing.Size;
using GdiRectangle = System.Drawing.Rectangle;

namespace CircleToSearch.Tests;

public sealed class OverlayCoordinateMapperTests
{
    [Fact]
    public void Maps_overscanned_dips_to_capture_relative_physical_pixels()
    {
        var mapper = new OverlayCoordinateMapper(1.5, overscan: true, new GdiSize(300, 200));

        Assert.Equal(new GdiPoint(15, 30), mapper.ToPhysical(new System.Windows.Point(11, 21)));
        Assert.Equal(new Rect(11, 21, 20, 10), mapper.ToDips(new GdiRectangle(15, 30, 30, 15)));
        Assert.Equal(new GdiRectangle(0, 0, 300, 200), mapper.CaptureBounds);
    }

    [Fact]
    public void Clamps_outside_points_without_adding_monitor_origin()
    {
        var mapper = new OverlayCoordinateMapper(2, false, new GdiSize(100, 50));

        Assert.Equal(new GdiPoint(0, 49), mapper.ToPhysical(new System.Windows.Point(-20, 200)));
    }
}
