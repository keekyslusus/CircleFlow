using CircleToSearch.Capture;
using CircleToSearch.MusicRecognition.Audio;
using CircleToSearch.Ui.Effects;
using NAudio.Wave;
using System.Windows;
using Xunit;

namespace CircleToSearch.Tests;

public sealed class AudioVisualizationTests
{
    [Fact]
    public void Float32_meter_combines_channels_by_energy()
    {
        float[] samples = [0.5f, -0.5f, 0.25f, -0.25f];
        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);

        var frame = AudioLevelMeter.Measure(
            bytes,
            WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2),
            TimeSpan.FromMilliseconds(10));

        Assert.Equal(Math.Sqrt((0.25 + 0.25 + 0.0625 + 0.0625) / 4), frame.Rms, 6);
        Assert.Equal(0.5, frame.Peak, 6);
    }

    [Fact]
    public void Pcm16_meter_handles_silence_and_full_scale_samples()
    {
        short[] samples = [0, short.MaxValue, short.MinValue, 0];
        var bytes = new byte[samples.Length * sizeof(short)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);

        var frame = AudioLevelMeter.Measure(bytes, new WaveFormat(44_100, 16, 2), default);

        Assert.InRange(frame.Rms, 0.7070, 0.7072);
        Assert.Equal(1, frame.Peak);
    }

    [Fact]
    public void Normalization_is_monotonic_and_bounded()
    {
        double[] amplitudes = [0, 0.00001, 0.001, 0.01, 0.1, 1, 2];
        var normalized = amplitudes.Select(AudioLevelNormalizer.Normalize).ToArray();

        Assert.All(normalized, value => Assert.InRange(value, 0, 1));
        Assert.Equal(normalized.Order().ToArray(), normalized);
        Assert.Equal(0, normalized[0]);
        Assert.Equal(1, normalized[^1]);
    }

    [Fact]
    public void Transient_detector_ignores_silence_and_steady_energy_then_rearms()
    {
        var detector = new AudioTransientDetector(new AudioTransientOptions(
            FastTimeConstantSeconds: 0.001,
            SlowTimeConstantSeconds: 1,
            MinimumLevel: 0.1,
            TriggerRatio: 1.5,
            RearmRatio: 1.1,
            RefractoryInterval: TimeSpan.FromMilliseconds(225)));

        Assert.False(detector.Process(new AudioLevelFrame(TimeSpan.Zero, 0, 0)).IsTransient);
        Assert.True(detector.Process(new AudioLevelFrame(TimeSpan.FromMilliseconds(100), 0.02, 0.04)).IsTransient);
        Assert.False(detector.Process(new AudioLevelFrame(TimeSpan.FromMilliseconds(150), 0.02, 0.04)).IsTransient);
        Assert.False(detector.Process(new AudioLevelFrame(TimeSpan.FromMilliseconds(400), 0, 0)).IsTransient);
        Assert.True(detector.Process(new AudioLevelFrame(TimeSpan.FromMilliseconds(500), 0.02, 0.04)).IsTransient);
    }

    [Theory]
    [InlineData(50, 50, 100, 100, 70.710678)]
    [InlineData(0, 0, 100, 100, 141.421356)]
    [InlineData(-10, -20, 100, 100, 162.788206)]
    public void Scene_ripple_radius_reaches_farthest_corner(
        double x,
        double y,
        double width,
        double height,
        double expected)
    {
        Assert.Equal(
            expected,
            SceneRippleHost.FarthestCornerDistance(new Point(x, y), new Size(width, height)),
            5);
    }

    [Fact]
    public void Provider_aware_visual_command_rejects_blank_provider()
    {
        using var bitmap = new System.Drawing.Bitmap(1, 1);
        var selection = new SelectionOutcome(new System.Drawing.Rectangle(0, 0, 1, 1), bitmap);

        Assert.Throws<ArgumentException>(() => new VisualSelection(selection, " "));
    }
}
