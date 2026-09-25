using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CircleToSearch.Ui;

namespace CircleToSearch.Shell.Onboarding;

// Steps slide like pages: forward pushes the current step out to the left, backward to the right.
internal sealed class OnboardingStepTransition
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(380);
    private readonly FrameworkElement _viewport;
    private readonly FrameworkElement[][] _steps;

    internal OnboardingStepTransition(FrameworkElement viewport, FrameworkElement[][] steps)
    {
        _viewport = viewport;
        _steps = steps;
        for (var index = 0; index < steps.Length; index++)
            foreach (var part in steps[index])
            {
                part.RenderTransform = new TranslateTransform();
                part.Visibility = index == 0 ? Visibility.Visible : Visibility.Hidden;
            }
    }

    internal int Current { get; private set; }

    internal void Show(int step)
    {
        if (step == Current) return;
        var direction = Math.Sign(step - Current);
        var previous = Current;
        Current = step;
        var animate = UiAnimationPolicy.Enabled && _viewport.IsLoaded && _viewport.IsVisible;
        var distance = _viewport.ActualWidth;
        for (var index = 0; index < _steps.Length; index++)
            foreach (var part in _steps[index])
            {
                var translation = (TranslateTransform)part.RenderTransform;
                if (index == step && animate)
                {
                    // A step that is still leaving turns around from where it is instead of jumping.
                    var from = part.Visibility == Visibility.Visible ? translation.X : direction * distance;
                    part.Visibility = Visibility.Visible;
                    Slide(translation, from, 0, null);
                }
                else if (index == previous && animate)
                {
                    var leaving = index;
                    Slide(translation, translation.X, -direction * distance, () =>
                    {
                        if (Current != leaving) Rest(part, translation, visible: false);
                    });
                }
                // A step still leaving from an earlier change finishes its exit instead of vanishing mid-slide.
                else if (!animate) Rest(part, translation, visible: index == step);
            }
    }

    private static void Slide(TranslateTransform translation, double from, double to, Action? completed)
    {
        translation.BeginAnimation(TranslateTransform.XProperty, null);
        translation.X = from;
        var animation = new DoubleAnimation(from, to, Duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
        if (completed is not null) animation.Completed += (_, _) => completed();
        translation.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    private static void Rest(FrameworkElement part, TranslateTransform translation, bool visible)
    {
        translation.BeginAnimation(TranslateTransform.XProperty, null);
        translation.X = 0;
        part.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
    }
}
