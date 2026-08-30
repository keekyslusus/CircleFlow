using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using CircleToSearch.MusicRecognition.Audio;
using CircleToSearch.Ui;

namespace CircleToSearch.Capture;

public sealed class AudioWaveformVisual : FrameworkElement, IDisposable
{
    private readonly Stopwatch _clock = new();
    private MusicVisualizationFrame _latest;
    private double _level;
    private double _impulse;
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
        if (_rendering || !OverlayVisualResources.AnimationsEnabled())
        {
            InvalidateVisual();
            return;
        }
        _rendering = true;
        _clock.Restart();
        CompositionTarget.Rendering += OnRendering;
    }

    public void Report(MusicVisualizationFrame frame)
    {
        _latest = frame;
        if (frame.IsTransient) _impulse = Math.Max(_impulse, frame.NormalizedPeak);
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
        for (var index = 0; index < 5; index++)
        {
            var x = (index + 0.5) * RenderSize.Width / 5;
            var phase = index * 0.85;
            var motion = OverlayVisualResources.AnimationsEnabled()
                ? Math.Sin(time * 5.6 + phase) * (RenderSize.Height * 0.18 + _level * 5 + _impulse * 3)
                : 0;
            var radius = Math.Max(2.2, RenderSize.Height * 0.095) + _level * 1.5 + _impulse;
            var sine = 0.5 + 0.5 * Math.Sin(time * 5.6 + phase);
            drawingContext.PushOpacity(Math.Clamp(0.4 + 0.6 * sine + _level * 0.12, 0.4, 1));
            drawingContext.DrawEllipse(brush, null, new Point(x, centerY + motion), radius, radius);
            drawingContext.Pop();
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var attack = _latest.NormalizedLevel > _level ? 0.32 : 0.09;
        _level += (_latest.NormalizedLevel - _level) * attack;
        _impulse *= 0.88;
        InvalidateVisual();
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
