using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using CircleToSearch.MusicRecognition.Audio;
using CircleToSearch.Ui;

namespace CircleToSearch.Capture;

public sealed class AudioWaveformVisual : FrameworkElement, IDisposable
{
    internal const int BarCount = 6;
    internal const double BarWidth = 7;
    internal const double BarGap = 10;
    internal const double BarGroupWidth = BarCount * BarWidth + (BarCount - 1) * BarGap;
    private const double BaseBarHeight = 8;
    private const double LevelSensitivity = 1.534;
    private static readonly double[] MaximumBarHeights = [22, 31, 40, 36, 30, 22];

    private readonly Stopwatch _clock = new();
    private readonly AudioWaveformDynamics _dynamics = new();
    private TimeSpan _previousRenderTime;
    private bool _rendering;

    public AudioWaveformVisual(bool? lightTheme = null)
    {
        Width = 170;
        Height = 40;
        IsHitTestVisible = false;
    }

    internal bool IsRendering => _rendering;

    public void Start()
    {
        if (_rendering)
        {
            InvalidateVisual();
            return;
        }

        _dynamics.Reset();
        _previousRenderTime = TimeSpan.Zero;
        if (!OverlayVisualResources.AnimationsEnabled())
        {
            _clock.Reset();
            InvalidateVisual();
            return;
        }

        _rendering = true;
        _clock.Restart();
        CompositionTarget.Rendering += OnRendering;
    }

    public void Report(MusicVisualizationFrame frame)
    {
        _dynamics.Report(frame.NormalizedLevel);
    }

    public void Stop()
    {
        if (!_rendering) return;
        _rendering = false;
        CompositionTarget.Rendering -= OnRendering;
        _clock.Stop();
        InvalidateVisual();
    }

    public void Dispose() => Stop();

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var brush = Frozen(SystemAccentColor.Read());
        var centerY = RenderSize.Height / 2;
        var time = _clock.Elapsed.TotalSeconds;
        var animationsEnabled = OverlayVisualResources.AnimationsEnabled();
        var left = (RenderSize.Width - BarGroupWidth) / 2;
        for (var index = 0; index < BarCount; index++)
        {
            var idleCarrier = Math.Sin(time * 2.4 + index * 0.8);
            var variationCarrier = Math.Sin(time * 6.1 + index * 1.16);
            var appearance = CalculateBarAppearance(
                RenderSize.Height,
                index,
                _dynamics.Level,
                idleCarrier,
                variationCarrier,
                animationsEnabled);
            var x = left + index * (BarWidth + BarGap);
            var bounds = new Rect(
                x,
                centerY - appearance.Height / 2,
                BarWidth,
                appearance.Height);
            drawingContext.PushOpacity(appearance.Opacity);
            drawingContext.DrawRoundedRectangle(brush, null, bounds, BarWidth / 2, BarWidth / 2);
            drawingContext.Pop();
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var renderTime = _clock.Elapsed;
        _dynamics.Advance((renderTime - _previousRenderTime).TotalSeconds);
        _previousRenderTime = renderTime;
        InvalidateVisual();
    }

    internal static AudioWaveformBarAppearance CalculateBarAppearance(
        double availableHeight,
        int barIndex,
        double level,
        double idleCarrier,
        double variationCarrier,
        bool animationsEnabled)
    {
        var safeAvailableHeight = double.IsFinite(availableHeight)
            ? Math.Max(0, availableHeight)
            : 0;
        if (!animationsEnabled)
        {
            return new AudioWaveformBarAppearance(Math.Min(12, safeAvailableHeight), 0.88);
        }

        var safeLevel = double.IsFinite(level) ? Math.Clamp(level, 0, 1) : 0;
        var reactiveLevel = Math.Clamp(safeLevel * LevelSensitivity, 0, 1);
        var safeIdleCarrier = double.IsFinite(idleCarrier) ? Math.Clamp(idleCarrier, -1, 1) : 0;
        var safeVariationCarrier = double.IsFinite(variationCarrier)
            ? Math.Clamp(variationCarrier, -1, 1)
            : 0;
        var idleEnergy = 0.055 + safeIdleCarrier * 0.018;
        var levelVariation = 0.84 + safeVariationCarrier * 0.16;
        var energy = Math.Clamp(idleEnergy + reactiveLevel * levelVariation, 0, 1);
        var maximumHeight = Math.Min(MaximumBarHeights[barIndex], safeAvailableHeight);
        var baseHeight = Math.Min(BaseBarHeight, maximumHeight);
        var height = baseHeight + (maximumHeight - baseHeight) * energy;
        var opacity = Math.Clamp(0.72 + energy * 0.28, 0, 1);
        return new AudioWaveformBarAppearance(height, opacity);
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}

internal readonly record struct AudioWaveformBarAppearance(
    double Height,
    double Opacity);
