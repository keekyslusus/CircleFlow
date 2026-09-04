using CircleToSearch.Capture;
using CircleToSearch.MusicRecognition.Audio;
using CircleToSearch.Ui;
using CircleToSearch.Ui.Effects;
using NAudio.Wave;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
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
            Sensitivity: 0.7,
            FastTimeConstantSeconds: 0.001,
            SlowTimeConstantSeconds: 1,
            MinimumLevel: 0.1,
            RefractoryInterval: TimeSpan.FromMilliseconds(225)));

        Assert.False(detector.Process(new AudioLevelFrame(TimeSpan.Zero, 0, 0)).IsTransient);
        Assert.True(detector.Process(new AudioLevelFrame(TimeSpan.FromMilliseconds(100), 0.02, 0.04)).IsTransient);
        Assert.False(detector.Process(new AudioLevelFrame(TimeSpan.FromMilliseconds(150), 0.02, 0.04)).IsTransient);
        Assert.False(detector.Process(new AudioLevelFrame(TimeSpan.FromMilliseconds(400), 0, 0)).IsTransient);
        Assert.True(detector.Process(new AudioLevelFrame(TimeSpan.FromMilliseconds(500), 0.02, 0.04)).IsTransient);
    }

    [Fact]
    public void Transient_detector_follows_beats_when_music_is_already_playing()
    {
        var detector = new AudioTransientDetector(new AudioTransientOptions(Sensitivity: 0.78));
        var elapsed = TimeSpan.Zero;

        for (var index = 0; index < 12; index++)
            Assert.False(ProcessNormalized(detector, ref elapsed, 0.72).IsTransient);

        Assert.True(ProcessNormalized(detector, ref elapsed, 0.9).IsTransient);
        for (var index = 0; index < 8; index++)
            Assert.False(ProcessNormalized(detector, ref elapsed, 0.72).IsTransient);
        Assert.True(ProcessNormalized(detector, ref elapsed, 0.9).IsTransient);
    }

    [Fact]
    public void Higher_sensitivity_detects_smaller_level_changes()
    {
        var sensitive = new AudioTransientDetector(new AudioTransientOptions(Sensitivity: 0.9));
        var selective = new AudioTransientDetector(new AudioTransientOptions(Sensitivity: 0.2));
        var sensitiveElapsed = TimeSpan.Zero;
        var selectiveElapsed = TimeSpan.Zero;

        for (var index = 0; index < 12; index++)
        {
            ProcessNormalized(sensitive, ref sensitiveElapsed, 0.72);
            ProcessNormalized(selective, ref selectiveElapsed, 0.72);
        }

        Assert.True(ProcessNormalized(sensitive, ref sensitiveElapsed, 0.8).IsTransient);
        Assert.False(ProcessNormalized(selective, ref selectiveElapsed, 0.8).IsTransient);
    }

    [Theory]
    [InlineData(50, 50, 100, 100, 70.710678)]
    [InlineData(0, 0, 100, 100, 141.421356)]
    [InlineData(-10, -20, 100, 100, 162.788206)]
    public void Entrance_particle_wave_reaches_farthest_corner(
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
    public void Entrance_scene_effect_contains_a_wash_and_particles_without_a_ring()
    {
        var failure = RunOnSta(() =>
        {
            var canvas = new Canvas { Width = 640, Height = 400 };
            canvas.Measure(new Size(640, 400));
            canvas.Arrange(new Rect(0, 0, 640, 400));
            using var host = new SceneRippleHost(canvas, animationsEnabled: true);

            host.Emit(new SceneRippleRequest(
                new Point(320, 200),
                SceneRipplePreset.Entrance,
                1));

            var effect = Assert.Single(canvas.Children.OfType<Canvas>());
            var wash = Assert.Single(effect.Children.OfType<Rectangle>());
            Assert.Equal(
                PluginPalette.WithAlpha(PluginPalette.SceneRippleAudio, 0.15),
                Assert.IsType<SolidColorBrush>(wash.Fill).Color);
            var particles = effect.Children.OfType<Ellipse>().ToArray();
            Assert.True(particles.Length > 20);
            Assert.All(particles, particle => Assert.Null(particle.Stroke));
            Assert.Equal(1, host.ActiveCount);
        });

        Assert.Null(failure);
    }

    [Theory]
    [InlineData(SceneRipplePreset.AudioTransient, 0.5)]
    [InlineData(SceneRipplePreset.MusicMatch, 1)]
    [InlineData(SceneRipplePreset.TranslationComplete, 1)]
    public void Scene_effects_are_fullscreen_particle_waves_without_rings(
        SceneRipplePreset preset,
        double intensity)
    {
        var failure = RunOnSta(() =>
        {
            var canvas = new Canvas { Width = 640, Height = 400 };
            canvas.Measure(new Size(640, 400));
            canvas.Arrange(new Rect(0, 0, 640, 400));
            using var host = new SceneRippleHost(canvas, animationsEnabled: true);

            host.Emit(new SceneRippleRequest(new Point(320, 200), preset, intensity));

            var effect = Assert.Single(canvas.Children.OfType<Canvas>());
            var wash = Assert.Single(effect.Children.OfType<Rectangle>());
            Assert.NotNull(wash.Fill);
            Assert.True(wash.HasAnimatedProperties);
            var particles = effect.Children.OfType<Ellipse>().ToArray();
            Assert.True(particles.Length >= 25);
            Assert.All(particles, particle =>
            {
                Assert.Null(particle.Stroke);
                Assert.NotNull(particle.Fill);
                Assert.True(particle.HasAnimatedProperties);
            });
            Assert.True(
                particles.Max(Canvas.GetLeft) - particles.Min(Canvas.GetLeft) > canvas.ActualWidth * 0.65,
                "particles should span most of the overlay width");
            Assert.True(
                particles.Max(Canvas.GetTop) - particles.Min(Canvas.GetTop) > canvas.ActualHeight * 0.65,
                "particles should span most of the overlay height");
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Music_match_scene_effect_uses_system_accent()
    {
        var failure = RunOnSta(() =>
        {
            var canvas = new Canvas { Width = 640, Height = 400 };
            canvas.Measure(new Size(640, 400));
            canvas.Arrange(new Rect(0, 0, 640, 400));
            using var host = new SceneRippleHost(canvas, animationsEnabled: true);

            host.Emit(new SceneRippleRequest(
                new Point(320, 200),
                SceneRipplePreset.MusicMatch,
                1));

            var effect = Assert.Single(canvas.Children.OfType<Canvas>());
            var wash = Assert.Single(effect.Children.OfType<Rectangle>());
            var fill = Assert.IsType<SolidColorBrush>(wash.Fill);
            Assert.Equal(
                PluginPalette.WithAlpha(SystemAccentColor.Read(), 0.07),
                fill.Color);
        });

        Assert.Null(failure);
    }

    [Fact]
    public void Provider_aware_visual_command_rejects_blank_provider()
    {
        using var bitmap = new System.Drawing.Bitmap(1, 1);
        var selection = new SelectionOutcome(new System.Drawing.Rectangle(0, 0, 1, 1), bitmap);

        Assert.Throws<ArgumentException>(() => new VisualSelection(selection, " "));
    }

    private static Exception? RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(10));
        Assert.False(thread.IsAlive, "the STA thread did not finish in time");
        return failure;
    }

    private static MusicVisualizationFrame ProcessNormalized(
        AudioTransientDetector detector,
        ref TimeSpan elapsed,
        double normalizedLevel)
    {
        elapsed += TimeSpan.FromMilliseconds(40);
        var decibels = -60 + Math.Clamp(normalizedLevel, 0, 1) * 54;
        var amplitude = Math.Pow(10, decibels / 20);
        return detector.Process(new AudioLevelFrame(elapsed, amplitude, amplitude));
    }
}
