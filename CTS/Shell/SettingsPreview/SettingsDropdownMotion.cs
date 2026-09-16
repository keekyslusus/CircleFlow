using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CircleToSearch.Ui;

namespace CircleToSearch.Shell.SettingsPreview;

internal sealed class SettingsDropdownMotion : IDisposable
{
    private readonly ComboBox _combo;
    private readonly Popup _popup;
    private readonly FrameworkElement _surface;
    private readonly RotateTransform _rotation;
    private readonly RectangleGeometry _reveal = new();
    private int _rotationVersion;

    internal SettingsDropdownMotion(ComboBox combo)
    {
        _combo = combo;
        combo.ApplyTemplate();
        _popup = (Popup)combo.Template.FindName("PART_Popup", combo);
        _surface = (FrameworkElement)combo.Template.FindName("DropdownSurface", combo);
        var arrow = (FrameworkElement)combo.Template.FindName("DropdownArrow", combo);
        _rotation = ((RotateTransform)arrow.RenderTransform).Clone();
        arrow.RenderTransform = _rotation;
        _surface.Clip = _reveal;
        _popup.Opened += OnOpened;
        _popup.Closed += OnClosed;
        combo.IsVisibleChanged += OnVisibilityChanged;
        combo.Unloaded += OnUnloaded;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        RotateTo(180, UiAnimationPolicy.Enabled);
        _surface.UpdateLayout();
        var bounds = new Rect(_surface.RenderSize);
        _reveal.BeginAnimation(RectangleGeometry.RectProperty, null);
        if (!UiAnimationPolicy.Enabled)
        {
            _reveal.Rect = bounds;
            return;
        }

        // Windows may place a popup above its anchor near the screen edge.
        var above = _surface.PointToScreen(new Point()).Y < _combo.PointToScreen(new Point()).Y;
        var collapsed = new Rect(0, above ? bounds.Height : 0, bounds.Width, 0);
        _reveal.Rect = collapsed;
        var animation = new RectAnimation(collapsed, bounds, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        _reveal.BeginAnimation(RectangleGeometry.RectProperty, animation);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        ResetReveal();
        RotateTo(0, UiAnimationPolicy.Enabled && _combo.IsVisible);
    }

    private void RotateTo(double angle, bool animate)
    {
        var current = _rotation.Angle;
        var version = ++_rotationVersion;
        _rotation.BeginAnimation(RotateTransform.AngleProperty, null);
        _rotation.Angle = animate ? current : angle;
        if (!animate) return;
        var animation = new DoubleAnimation(current, angle, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        animation.Completed += (_, _) =>
        {
            if (version != _rotationVersion) return;
            _rotation.Angle = angle;
            _rotation.BeginAnimation(RotateTransform.AngleProperty, null);
        };
        _rotation.BeginAnimation(RotateTransform.AngleProperty, animation);
    }

    private void ResetReveal()
    {
        _reveal.BeginAnimation(RectangleGeometry.RectProperty, null);
        _reveal.Rect = Rect.Empty;
    }

    private void Reset()
    {
        _combo.IsDropDownOpen = false;
        ResetReveal();
        RotateTo(0, animate: false);
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!_combo.IsVisible) Reset();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Reset();

    public void Dispose()
    {
        _popup.Opened -= OnOpened;
        _popup.Closed -= OnClosed;
        _combo.IsVisibleChanged -= OnVisibilityChanged;
        _combo.Unloaded -= OnUnloaded;
        Reset();
    }
}
