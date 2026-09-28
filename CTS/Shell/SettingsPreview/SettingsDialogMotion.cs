using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CircleToSearch.Ui;

namespace CircleToSearch.Shell.SettingsPreview;

internal sealed class SettingsDialogMotion : IDisposable
{
    private readonly FrameworkElement _layer;
    private readonly FrameworkElement _surface;
    private readonly FrameworkElement _scrim;
    private readonly Action _closed;
    private readonly ScaleTransform _scale = new();
    private readonly TranslateTransform _translation = new();
    private int _version;

    internal SettingsDialogMotion(FrameworkElement layer, FrameworkElement surface, FrameworkElement scrim, Action closed)
    {
        _layer = layer;
        _surface = surface;
        _scrim = scrim;
        _closed = closed;
        surface.RenderTransform = new TransformGroup { Children = { _scale, _translation } };
        layer.IsVisibleChanged += OnVisibilityChanged;
        layer.Unloaded += OnUnloaded;
    }

    internal bool IsOpen { get; private set; }

    internal void Open()
    {
        if (_layer.Visibility != Visibility.Visible) SetValues(false, 0.96, 12);
        IsOpen = true;
        _surface.IsHitTestVisible = true;
        KeyboardNavigation.SetTabNavigation(_surface, KeyboardNavigationMode.Continue);
        _layer.Visibility = Visibility.Visible;
        Animate(open: true);
    }

    internal void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _surface.IsHitTestVisible = false;
        KeyboardNavigation.SetTabNavigation(_surface, KeyboardNavigationMode.None);
        // Keep the backdrop modal until it has faded, with focus away from the departing buttons.
        _layer.Focus();
        Animate(open: false);
    }

    private void Animate(bool open)
    {
        var version = ++_version;
        if (!UiAnimationPolicy.Enabled || !_layer.IsVisible)
        {
            if (open) SetValues(true, 1, 0);
            else FinishClose();
            return;
        }
        var duration = TimeSpan.FromMilliseconds(open ? 200 : 140);
        var easing = new CubicEase { EasingMode = open ? EasingMode.EaseOut : EasingMode.EaseIn };
        SettingsMotionValue.Animate(_scale, ScaleTransform.ScaleXProperty, open ? 1 : 0.98, duration, easing);
        SettingsMotionValue.Animate(_scale, ScaleTransform.ScaleYProperty, open ? 1 : 0.98, duration, easing);
        SettingsMotionValue.Animate(_translation, TranslateTransform.YProperty, open ? 0 : 8, duration, easing);
        SettingsMotionValue.Animate(_scrim, UIElement.OpacityProperty, open ? 1 : 0, duration, easing);
        SettingsMotionValue.Animate(_surface, UIElement.OpacityProperty, open ? 1 : 0, duration, easing, () =>
        {
            if (version != _version) return;
            if (open) SetValues(true, 1, 0);
            else FinishClose();
        });
    }

    private void SetValues(bool visible, double scale, double y)
    {
        _version++;
        _surface.BeginAnimation(UIElement.OpacityProperty, null);
        _scrim.BeginAnimation(UIElement.OpacityProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _translation.BeginAnimation(TranslateTransform.YProperty, null);
        _surface.Opacity = _scrim.Opacity = visible ? 1 : 0;
        _scale.ScaleX = _scale.ScaleY = scale;
        _translation.Y = y;
    }

    private void FinishClose()
    {
        var wasVisible = _layer.Visibility == Visibility.Visible;
        IsOpen = false;
        SetValues(false, 0.96, 12);
        _layer.Visibility = Visibility.Collapsed;
        if (wasVisible) _closed();
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!_layer.IsVisible && _layer.Visibility == Visibility.Visible) FinishClose();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => FinishClose();

    public void Dispose()
    {
        _layer.IsVisibleChanged -= OnVisibilityChanged;
        _layer.Unloaded -= OnUnloaded;
        FinishClose();
    }
}
