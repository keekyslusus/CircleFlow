using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CircleToSearch.Ui;

namespace CircleToSearch.Shell.SettingsPreview;

internal sealed class SettingsPageTransition : IDisposable
{
    private readonly ScrollViewer _scroll;
    private readonly FrameworkElement _surface;
    private readonly FrameworkElement[] _pages;
    private readonly TranslateTransform _translation = new();
    private FrameworkElement _current;
    private FrameworkElement _target;
    private bool _exiting;
    private bool _disposed;
    private int _generation;

    internal SettingsPageTransition(ScrollViewer scroll, FrameworkElement surface, FrameworkElement[] pages)
    {
        _scroll = scroll;
        _surface = surface;
        _pages = pages;
        _current = _target = pages.Single(page => page.Visibility == Visibility.Visible);
        surface.RenderTransform = _translation;
        surface.IsVisibleChanged += OnVisibilityChanged;
        surface.Unloaded += OnUnloaded;
    }

    internal void Show(FrameworkElement page)
    {
        if (_disposed || ReferenceEquals(page, _target)) return;
        _target = page;
        if (!_surface.IsLoaded || !_surface.IsVisible || !UiAnimationPolicy.Enabled)
        {
            Finish();
            return;
        }

        _scroll.IsHitTestVisible = false;
        KeyboardNavigation.SetTabNavigation(_surface, KeyboardNavigationMode.None);
        // While the old page exits, only replace the destination. Repeated clicks never queue transitions.
        if (_exiting) return;
        _exiting = true;
        Animate(_translation.Y + 14, 0, 90, EasingMode.EaseIn, () =>
        {
            _exiting = false;
            ApplyTarget();
            _translation.Y = 22;
            _surface.Opacity = 0;
            Animate(0, 1, 180, EasingMode.EaseOut, Finish);
        });
    }

    private void ApplyTarget()
    {
        if (ReferenceEquals(_current, _target)) return;
        foreach (var page in _pages)
            page.Visibility = ReferenceEquals(page, _target) ? Visibility.Visible : Visibility.Collapsed;
        _current = _target;
        _scroll.ScrollToTop();
        // Resolve the new extent and queued offset while the content is transparent.
        _scroll.UpdateLayout();
    }

    private void Animate(double y, double opacity, int milliseconds, EasingMode easing, Action completed)
    {
        var fromY = _translation.Y;
        var fromOpacity = _surface.Opacity;
        StopAnimations();
        var generation = _generation;
        var duration = TimeSpan.FromMilliseconds(milliseconds);
        _translation.Y = fromY;
        _surface.Opacity = fromOpacity;
        _translation.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, y, duration)
        {
            EasingFunction = new CubicEase { EasingMode = easing },
        });
        var fade = new DoubleAnimation(fromOpacity, opacity, duration)
        {
            EasingFunction = new CubicEase { EasingMode = easing },
        };
        fade.Completed += (_, _) =>
        {
            if (_disposed || generation != _generation) return;
            StopAnimations();
            _translation.Y = y;
            _surface.Opacity = opacity;
            completed();
        };
        _surface.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private void StopAnimations()
    {
        _generation++;
        _translation.BeginAnimation(TranslateTransform.YProperty, null);
        _surface.BeginAnimation(UIElement.OpacityProperty, null);
    }

    private void Finish()
    {
        StopAnimations();
        _exiting = false;
        ApplyTarget();
        _translation.Y = 0;
        _surface.Opacity = 1;
        _scroll.IsHitTestVisible = true;
        KeyboardNavigation.SetTabNavigation(_surface, KeyboardNavigationMode.Continue);
    }

    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!_surface.IsVisible) Finish();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Finish();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _surface.IsVisibleChanged -= OnVisibilityChanged;
        _surface.Unloaded -= OnUnloaded;
        Finish();
    }
}
