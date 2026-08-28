using System.Drawing;
using CircleToSearch.Capture;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class LassoPathSamplerTests
{
    [Fact]
    public void First_point_is_always_accepted()
    {
        var sampler = new LassoPathSampler(minDistancePx: 3);

        Assert.True(sampler.Add(new Point(-50, 120)));
        Assert.Single(sampler.Points);
    }

    [Fact]
    public void Jitter_below_threshold_is_discarded()
    {
        var sampler = new LassoPathSampler(minDistancePx: 3);

        Assert.True(sampler.Add(new Point(10, 10)));
        Assert.False(sampler.Add(new Point(11, 11)));
        Assert.False(sampler.Add(new Point(12, 10)));
        Assert.Single(sampler.Points);
    }

    [Fact]
    public void Movement_beyond_threshold_is_accepted()
    {
        var sampler = new LassoPathSampler(minDistancePx: 3);

        sampler.Add(new Point(10, 10));

        Assert.True(sampler.Add(new Point(14, 10)));
        Assert.Equal(2, sampler.Points.Count);
    }

    [Fact]
    public void Fast_long_segments_are_accepted_without_interpolation()
    {
        var sampler = new LassoPathSampler(minDistancePx: 3);

        sampler.Add(new Point(0, 0));

        Assert.True(sampler.Add(new Point(900, 500)));
        Assert.Equal(2, sampler.Points.Count);
    }

    [Fact]
    public void Threshold_is_measured_from_the_last_accepted_point()
    {
        var sampler = new LassoPathSampler(minDistancePx: 3);

        sampler.Add(new Point(0, 0));
        Assert.False(sampler.Add(new Point(2, 2)));

        Assert.True(sampler.Add(new Point(4, 0)));
        Assert.Equal([new Point(0, 0), new Point(4, 0)], sampler.Points);
    }

    [Fact]
    public void Negative_monitor_coordinates_are_supported()
    {
        var sampler = new LassoPathSampler(minDistancePx: 3);

        sampler.Add(new Point(-1920, -400));

        Assert.True(sampler.Add(new Point(-1800, -100)));
        Assert.Equal([new Point(-1920, -400), new Point(-1800, -100)], sampler.Points);
    }

    [Fact]
    public void Final_point_is_kept_even_below_threshold()
    {
        var sampler = new LassoPathSampler(minDistancePx: 3);

        sampler.Add(new Point(10, 10));
        sampler.Add(new Point(11, 11));

        Assert.True(sampler.AddFinal(new Point(13, 10)));
        Assert.Equal([new Point(10, 10), new Point(13, 10)], sampler.Points);
    }

    [Fact]
    public void Final_point_duplicate_is_ignored()
    {
        var sampler = new LassoPathSampler(minDistancePx: 3);

        sampler.Add(new Point(10, 10));

        Assert.False(sampler.AddFinal(new Point(10, 10)));
        Assert.Single(sampler.Points);
    }

    [Fact]
    public void Click_without_movement_keeps_a_single_point()
    {
        var sampler = new LassoPathSampler(minDistancePx: 3);

        sampler.Add(new Point(42, 42));
        sampler.AddFinal(new Point(42, 42));

        Assert.Equal([new Point(42, 42)], sampler.Points);
    }

    [Fact]
    public void Reset_clears_the_path()
    {
        var sampler = new LassoPathSampler(minDistancePx: 3);

        sampler.Add(new Point(1, 1));
        sampler.Add(new Point(50, 50));
        sampler.Reset();
        sampler.Add(new Point(7, 7));

        Assert.Equal([new Point(7, 7)], sampler.Points);
    }
}
