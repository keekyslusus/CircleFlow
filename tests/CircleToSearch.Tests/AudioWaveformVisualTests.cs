using CircleToSearch.Capture;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class AudioWaveformVisualTests
{
    [Fact]
    public void Invalid_levels_are_sanitized_and_clamped()
    {
        var dynamics = new AudioWaveformDynamics();

        dynamics.Report(-0.4);
        Assert.Equal(0, dynamics.TargetLevel);
        dynamics.Report(double.NaN);
        Assert.Equal(0, dynamics.TargetLevel);
        dynamics.Report(double.PositiveInfinity);
        Assert.Equal(0, dynamics.TargetLevel);
        dynamics.Report(double.NegativeInfinity);
        Assert.Equal(0, dynamics.TargetLevel);
        dynamics.Report(1.4);

        Assert.Equal(1, dynamics.TargetLevel);
    }

    [Fact]
    public void Advance_attacks_faster_than_it_releases()
    {
        var dynamics = new AudioWaveformDynamics();
        dynamics.Report(1);

        dynamics.Advance(0.05);
        var attackedLevel = dynamics.Level;

        dynamics.Report(0);
        dynamics.Advance(0.05);
        var releasedLevel = dynamics.Level;
        var attackAmount = attackedLevel;
        var releaseAmount = attackedLevel - releasedLevel;

        Assert.InRange(attackedLevel, 0, 1);
        Assert.True(releaseAmount < attackAmount);
        Assert.True(releasedLevel > 0);
    }

    [Fact]
    public void Reset_clears_target_and_smoothed_level()
    {
        var dynamics = new AudioWaveformDynamics();
        dynamics.Report(1);
        dynamics.Advance(0.05);

        dynamics.Reset();

        Assert.Equal(0, dynamics.TargetLevel);
        Assert.Equal(0, dynamics.Level);
    }

    [Fact]
    public void Loud_bar_is_taller_and_more_opaque_than_idle_bar()
    {
        var idle = AudioWaveformVisual.CalculateBarAppearance(
            40, 2, 0, 1, 1, animationsEnabled: true);
        var loud = AudioWaveformVisual.CalculateBarAppearance(
            40, 2, 1, 1, 1, animationsEnabled: true);

        Assert.InRange(idle.Height, 8, 11);
        Assert.True(loud.Height > idle.Height);
        Assert.True(loud.Opacity > idle.Opacity);
        Assert.InRange(loud.Height, 8, 40);
        Assert.InRange(loud.Opacity, 0.72, 1);
    }

    [Fact]
    public void Moderate_audio_level_gets_the_visual_sensitivity_boost()
    {
        var appearance = AudioWaveformVisual.CalculateBarAppearance(
            40, 2, 0.5, 0, 0, animationsEnabled: true);

        Assert.InRange(appearance.Height, 30, 31);
    }

    [Fact]
    public void Six_bar_silhouette_matches_the_reference_proportions()
    {
        var heights = Enumerable.Range(0, AudioWaveformVisual.BarCount)
            .Select(index => AudioWaveformVisual.CalculateBarAppearance(
                40, index, 0.8, 0, 0, animationsEnabled: true).Height)
            .ToArray();

        Assert.Equal(6, heights.Length);
        Assert.True(heights[2] > heights[1]);
        Assert.True(heights[1] > heights[0]);
        Assert.True(heights[3] > heights[4]);
        Assert.True(heights[4] > heights[5]);
        Assert.Equal(92, AudioWaveformVisual.BarGroupWidth);
    }

    [Fact]
    public void Reduced_motion_returns_a_static_capsule()
    {
        var quiet = AudioWaveformVisual.CalculateBarAppearance(
            40, 0, 0, -1, -1, animationsEnabled: false);
        var loud = AudioWaveformVisual.CalculateBarAppearance(
            40, 2, 1, 1, 1, animationsEnabled: false);

        Assert.Equal(12, quiet.Height);
        Assert.Equal(quiet, loud);
    }
}
