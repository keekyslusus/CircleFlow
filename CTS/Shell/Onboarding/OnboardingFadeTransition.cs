using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CircleToSearch.Ui;

namespace CircleToSearch.Shell.Onboarding;

// Shows or hides one element with a fade and a slight scale. A hidden element keeps its layout space,
// so nothing around it moves while it animates.
internal sealed class OnboardingFadeTransition
{
    private const double HiddenScale = 0.92;
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(200);
    private readonly FrameworkElement _element;
    private readonly ScaleTransform _scale = new();
    private int _generation;

    internal OnboardingFadeTransition(FrameworkElement element, bool shown)
    {
        _element = element;
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = _scale;
        IsShown = shown;
        Set(shown);
    }

    internal bool IsShown { get; private set; }

    internal void Show(bool shown)
    {
        if (shown == IsShown) return;
        IsShown = shown;
        var generation = ++_generation;
        // The element itself is hidden whenever it is about to appear, so its container decides.
        var container = _element.Parent as UIElement ?? _element;
        if (!UiAnimationPolicy.Enabled || !_element.IsLoaded || !container.IsVisible)
        {
            Set(shown);
            return;
        }
        _element.Visibility = Visibility.Visible;
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var scale = shown ? 1 : HiddenScale;
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, Duration) { EasingFunction = easing });
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, Duration) { EasingFunction = easing });
        var fade = new DoubleAnimation(shown ? 1 : 0, Duration) { EasingFunction = easing };
        if (!shown)
            fade.Completed += (_, _) =>
            {
                if (generation == _generation) _element.Visibility = Visibility.Hidden;
            };
        _element.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private void Set(bool shown)
    {
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _element.BeginAnimation(UIElement.OpacityProperty, null);
        _scale.ScaleX = _scale.ScaleY = shown ? 1 : HiddenScale;
        _element.Opacity = shown ? 1 : 0;
        _element.Visibility = shown ? Visibility.Visible : Visibility.Hidden;
    }
}
