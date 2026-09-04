using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace CircleToSearch.Ui.Effects;

public sealed class SceneRippleHost : ISceneRippleSink, IDisposable
{
    private const int MaximumActiveEffects = 4;
    private const double ParticleDensity = 0.00012;
    private const int MaximumParticles = 200;
    private readonly Canvas _canvas;
    private readonly bool _animationsEnabled;
    private readonly Queue<FrameworkElement> _active = new();
    private bool _disposed;

    public SceneRippleHost(Canvas canvas) :
        this(canvas, Capture.OverlayVisualResources.AnimationsEnabled())
    {
    }

    internal SceneRippleHost(Canvas canvas, bool animationsEnabled)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _animationsEnabled = animationsEnabled;
        _canvas.IsHitTestVisible = false;
    }

    internal int ActiveCount => _active.Count;

    public void Emit(SceneRippleRequest request)
    {
        if (_disposed || !_animationsEnabled) return;
        while (_active.Count >= MaximumActiveEffects) Remove(_active.Dequeue());

        var size = new Size(Math.Max(0, _canvas.ActualWidth), Math.Max(0, _canvas.ActualHeight));
        if (size.Width <= 0 || size.Height <= 0) return;
        var profile = Profile(request.Preset, Math.Clamp(request.Intensity, 0, 1));
        var effect = new Canvas
        {
            Width = size.Width,
            Height = size.Height,
            IsHitTestVisible = false,
        };
        _canvas.Children.Add(effect);
        _active.Enqueue(effect);

        switch (request.Preset)
        {
            case SceneRipplePreset.Entrance:
                AddEntranceWash(effect, size, profile.Color);
                AddEntranceParticles(effect, request.Origin, size, profile.Color);
                break;
            case SceneRipplePreset.MusicMatch:
                AddMusicWash(effect, size, profile);
                AddFullscreenMusicParticles(effect, request.Origin, size, profile, intensity: 1);
                break;
            default:
                AddMusicWash(effect, size, profile);
                AddFullscreenMusicParticles(effect, request.Origin, size, profile, request.Intensity);
                break;
        }

        var lifetime = new DoubleAnimation(1, 1, profile.Lifetime);
        lifetime.Completed += (_, _) =>
        {
            Remove(effect);
            if (_active.Count > 0 && ReferenceEquals(_active.Peek(), effect)) _active.Dequeue();
            else RemoveFromQueue(effect);
        };
        effect.BeginAnimation(UIElement.OpacityProperty, lifetime);
    }

    private static void AddEntranceWash(Canvas effect, Size size, Color accent)
    {
        var wash = new Rectangle
        {
            Width = size.Width,
            Height = size.Height,
            Fill = Frozen(PluginPalette.WithAlpha(accent, 0.15)),
            Opacity = 0,
            IsHitTestVisible = false,
        };
        effect.Children.Add(wash);
        wash.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimationUsingKeyFrames
        {
            KeyFrames =
            {
                new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(550)))
                {
                    EasingFunction = EaseOut(),
                },
                new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(670))),
                new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(950))),
            },
        });
    }

    private static void AddEntranceParticles(Canvas effect, Point origin, Size size, Color accent)
    {
        var hardware = Capture.OverlayVisualResources.HardwareEffectsEnabled();
        var maximum = hardware ? MaximumParticles : 100;
        var minimum = hardware ? 80 : 48;
        var count = (int)Math.Clamp(size.Width * size.Height * ParticleDensity * 1.15, minimum, maximum);
        var maxRadius = FarthestCornerDistance(origin, size);
        var random = Random.Shared;

        for (var index = 0; index < count; index++)
        {
            var accentParticle = random.NextDouble() < 0.55;
            var wave = random.NextDouble();
            var angle = random.NextDouble() * 2 * Math.PI;
            var radius = wave * maxRadius * (0.9 + random.NextDouble() * 0.15);
            var diameter = 2 + random.NextDouble() * 1.6;
            var x = origin.X + Math.Cos(angle) * radius;
            var y = origin.Y + Math.Sin(angle) * radius;
            if (x < -4 || y < -4 || x > size.Width + 4 || y > size.Height + 4) continue;

            var particle = new Ellipse
            {
                Width = diameter,
                Height = diameter,
                Fill = Frozen(accentParticle
                    ? PluginPalette.WithAlpha(accent, 0.9)
                    : PluginPalette.EntranceParticle),
                Opacity = 0,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(particle, x - diameter / 2);
            Canvas.SetTop(particle, y - diameter / 2);
            effect.Children.Add(particle);

            var pulseMilliseconds = 320 + random.NextDouble() * 330;
            var beginMilliseconds = wave * 550 * (0.85 + random.NextDouble() * 0.3) +
                                    random.NextDouble() * 140;
            var peak = (accentParticle ? 0.3 : 0.38) + random.NextDouble() * 0.25;
            var twinkle = new DoubleAnimationUsingKeyFrames
            {
                BeginTime = TimeSpan.FromMilliseconds(beginMilliseconds),
                KeyFrames =
                {
                    new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                    new EasingDoubleKeyFrame(
                        peak,
                        KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(pulseMilliseconds * 0.35)))
                    {
                        EasingFunction = EaseOut(),
                    },
                    new EasingDoubleKeyFrame(
                        0,
                        KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(pulseMilliseconds)))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
                    },
                },
            };
            particle.BeginAnimation(UIElement.OpacityProperty, twinkle);
        }
    }

    private static void AddMusicWash(Canvas effect, Size size, ParticleProfile profile)
    {
        var wash = new Rectangle
        {
            Width = size.Width,
            Height = size.Height,
            Fill = Frozen(PluginPalette.WithAlpha(profile.Color, profile.WashAlpha)),
            Opacity = 0,
            IsHitTestVisible = false,
        };
        effect.Children.Add(wash);
        wash.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimationUsingKeyFrames
        {
            KeyFrames =
            {
                new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90)))
                {
                    EasingFunction = EaseOut(),
                },
                new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(profile.Lifetime))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
                },
            },
        });
    }

    private static void AddFullscreenMusicParticles(
        Canvas effect,
        Point origin,
        Size size,
        ParticleProfile profile,
        double intensity)
    {
        var normalized = Math.Clamp(intensity, 0, 1);
        var requestedCount = profile.MinimumParticles +
                             (profile.MaximumParticles - profile.MinimumParticles) * normalized;
        var referenceArea = 1920.0 * 1080.0;
        var areaScale = Math.Clamp(size.Width * size.Height / referenceArea, 0.75, 1.75);
        var count = (int)Math.Round(requestedCount * areaScale);
        var maxDistance = FarthestCornerDistance(origin, size);
        var random = Random.Shared;

        for (var index = 0; index < count; index++)
        {
            var accentParticle = random.NextDouble() < 0.68;
            var x = random.NextDouble() * size.Width;
            var y = random.NextDouble() * size.Height;
            var diameter = profile.MinimumDiameter +
                           random.NextDouble() * (profile.MaximumDiameter - profile.MinimumDiameter);
            var particle = new Ellipse
            {
                Width = diameter,
                Height = diameter,
                Fill = Frozen(accentParticle
                    ? PluginPalette.WithAlpha(profile.Color, 0.95)
                    : PluginPalette.EntranceParticle),
                Opacity = 0,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(particle, x - diameter / 2);
            Canvas.SetTop(particle, y - diameter / 2);
            effect.Children.Add(particle);

            var distance = Math.Sqrt(Math.Pow(x - origin.X, 2) + Math.Pow(y - origin.Y, 2));
            var wave = maxDistance <= 0 ? 0 : Math.Clamp(distance / maxDistance, 0, 1);
            var delay = TimeSpan.FromMilliseconds(
                wave * profile.WaveTravel.TotalMilliseconds +
                random.NextDouble() * profile.MaximumDelay.TotalMilliseconds);
            var duration = profile.Lifetime - delay;
            if (duration < TimeSpan.FromMilliseconds(220)) duration = TimeSpan.FromMilliseconds(220);
            var peak = profile.MinimumOpacity +
                       random.NextDouble() * (profile.MaximumOpacity - profile.MinimumOpacity);
            particle.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimationUsingKeyFrames
            {
                BeginTime = delay,
                KeyFrames =
                {
                    new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                    new EasingDoubleKeyFrame(peak, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70)))
                    {
                        EasingFunction = EaseOut(),
                    },
                    new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(duration))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
                    },
                },
            });
        }
    }

    public static double FarthestCornerDistance(Point origin, Size size)
    {
        var x = Math.Max(Math.Abs(origin.X), Math.Abs(size.Width - origin.X));
        var y = Math.Max(Math.Abs(origin.Y), Math.Abs(size.Height - origin.Y));
        return Math.Sqrt(x * x + y * y);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var ripple in _active) Remove(ripple);
        _active.Clear();
    }

    private static ParticleProfile Profile(SceneRipplePreset preset, double intensity) => preset switch
    {
        SceneRipplePreset.Entrance => new(
            PluginPalette.SceneRippleAudio,
            TimeSpan.FromMilliseconds(1150),
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            TimeSpan.Zero,
            TimeSpan.Zero),
        SceneRipplePreset.MusicMatch => new(
            SystemAccentColor.Read(),
            TimeSpan.FromMilliseconds(980),
            90,
            140,
            3.2,
            6.5,
            0.5,
            0.95,
            0.07,
            TimeSpan.FromMilliseconds(520),
            TimeSpan.FromMilliseconds(90)),
        SceneRipplePreset.TranslationComplete => new(
            SystemAccentColor.Read(),
            TimeSpan.FromMilliseconds(900),
            64,
            112,
            2.8,
            5.8,
            0.44,
            0.84,
            0.05,
            TimeSpan.FromMilliseconds(480),
            TimeSpan.FromMilliseconds(75)),
        _ => new(
            PluginPalette.SceneRippleAudio,
            TimeSpan.FromMilliseconds(760),
            36,
            88,
            2.8,
            5.4,
            0.38 + intensity * 0.1,
            0.65 + intensity * 0.25,
            0.025 + intensity * 0.035,
            TimeSpan.FromMilliseconds(420),
            TimeSpan.FromMilliseconds(55)),
    };

    private void Remove(FrameworkElement element)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        _canvas.Children.Remove(element);
    }

    private void RemoveFromQueue(FrameworkElement element)
    {
        if (_active.Count == 0) return;
        var retained = _active.Where(item => !ReferenceEquals(item, element)).ToArray();
        _active.Clear();
        foreach (var item in retained) _active.Enqueue(item);
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static IEasingFunction EaseOut() => new CubicEase { EasingMode = EasingMode.EaseOut };

    private sealed record ParticleProfile(
        Color Color,
        TimeSpan Lifetime,
        int MinimumParticles,
        int MaximumParticles,
        double MinimumDiameter,
        double MaximumDiameter,
        double MinimumOpacity,
        double MaximumOpacity,
        double WashAlpha,
        TimeSpan WaveTravel,
        TimeSpan MaximumDelay);
}
