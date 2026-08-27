using System.Drawing;
using CircleToSearch.Capture;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class LassoBoundsCalculatorTests
{
    private static readonly Rectangle Monitor = new(0, 0, 800, 600);

    [Fact]
    public void Normal_lasso_bounds_include_padding()
    {
        Point[] path = [new(10, 10), new(50, 10), new(50, 60), new(10, 60)];

        var bounds = LassoBoundsCalculator.Calculate(path, Monitor, paddingPx: 8, minDiagonalPx: 12);

        Assert.Equal(new Rectangle(2, 2, 56, 66), bounds);
    }

    [Fact]
    public void Open_path_is_bounded_by_its_points()
    {
        Point[] path = [new(20, 30), new(40, 35)];

        var bounds = LassoBoundsCalculator.Calculate(path, Monitor, paddingPx: 8, minDiagonalPx: 12);

        Assert.Equal(new Rectangle(12, 22, 36, 21), bounds);
    }

    [Fact]
    public void Tiny_gesture_is_rejected()
    {
        Point[] path = [new(10, 10), new(12, 11)];

        var bounds = LassoBoundsCalculator.Calculate(path, Monitor, paddingPx: 8, minDiagonalPx: 12);

        Assert.Null(bounds);
    }

    [Fact]
    public void Empty_path_is_rejected()
    {
        Assert.Null(LassoBoundsCalculator.Calculate([], Monitor, paddingPx: 8, minDiagonalPx: 12));
    }

    [Fact]
    public void Bounds_are_clamped_to_the_top_left_edge()
    {
        Point[] path = [new(2, 2), new(30, 30)];

        var bounds = LassoBoundsCalculator.Calculate(path, Monitor, paddingPx: 8, minDiagonalPx: 12);

        Assert.Equal(new Rectangle(0, 0, 38, 38), bounds);
    }

    [Fact]
    public void Bounds_are_clamped_to_the_bottom_right_edge()
    {
        Point[] path = [new(780, 580), new(795, 595)];

        var bounds = LassoBoundsCalculator.Calculate(path, Monitor, paddingPx: 8, minDiagonalPx: 12);

        Assert.Equal(new Rectangle(772, 572, 28, 28), bounds);
    }

    [Fact]
    public void Full_monitor_selection_fills_the_monitor()
    {
        Point[] path = [new(0, 0), new(800, 600)];

        var bounds = LassoBoundsCalculator.Calculate(path, Monitor, paddingPx: 8, minDiagonalPx: 12);

        Assert.Equal(Monitor, bounds);
    }

    [Fact]
    public void Selection_off_the_monitor_yields_nothing()
    {
        Point[] path = [new(900, 700), new(1000, 800)];

        var bounds = LassoBoundsCalculator.Calculate(path, Monitor, paddingPx: 8, minDiagonalPx: 12);

        Assert.Null(bounds);
    }
}
