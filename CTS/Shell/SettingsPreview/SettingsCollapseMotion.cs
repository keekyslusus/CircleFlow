using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CircleToSearch.Ui;

namespace CircleToSearch.Shell.SettingsPreview;

// Folds a section to zero height and back, so the content below it slides instead of jumping.
internal sealed class SettingsCollapseMotion
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(240);
    private readonly FrameworkElement _element;
    private int _version;

    internal SettingsCollapseMotion(FrameworkElement element)
    {
        _element = element;
        element.ClipToBounds = true;
        IsExpanded = element.Visibility == Visibility.Visible;
    }

    internal bool IsExpanded { get; private set; }

    internal void Set(bool expanded)
    {
        if (expanded == IsExpanded) return;
        IsExpanded = expanded;
        var version = ++_version;
        // While folding, ActualHeight stays at the unclipped content size; Height is the space on screen.
        var from = _element.Visibility != Visibility.Visible ? 0
            : double.IsNaN(_element.Height) ? _element.ActualHeight : _element.Height;
        var fromOpacity = _element.Visibility == Visibility.Visible ? _element.Opacity : 0;
        Stop();
        if (!UiAnimationPolicy.Enabled || VisualTreeHelper.GetParent(_element) is not UIElement { IsVisible: true } parent)
        {
            Finish(expanded);
            return;
        }

        _element.Visibility = Visibility.Visible;
        var to = 0.0;
        if (expanded)
        {
            _element.Height = double.NaN;
            parent.UpdateLayout();
            to = _element.ActualHeight;
        }
        _element.Height = from;
        _element.Opacity = fromOpacity;
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
        MotionValue.Animate(_element, FrameworkElement.HeightProperty, to, Duration, easing, () =>
        {
            if (version == _version) Finish(expanded);
        });
        MotionValue.Animate(_element, UIElement.OpacityProperty, expanded ? 1 : 0, Duration, easing);
    }

    private void Finish(bool expanded)
    {
        Stop();
        _element.Height = double.NaN;
        _element.Opacity = expanded ? 1 : 0;
        _element.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Stop()
    {
        _element.BeginAnimation(FrameworkElement.HeightProperty, null);
        _element.BeginAnimation(UIElement.OpacityProperty, null);
    }
}
