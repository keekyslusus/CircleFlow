using System.Windows;
using System.Windows.Media.Animation;

namespace CircleToSearch.Shell.SettingsPreview;

internal static class SettingsMotionValue
{
    // Starts from the value on screen, so a reversed transition continues instead of jumping.
    internal static void Animate(DependencyObject target, DependencyProperty property,
        double value, TimeSpan duration, IEasingFunction easing, Action? completed = null)
    {
        var current = (double)target.GetValue(property);
        var animation = new DoubleAnimation(current, value, duration) { EasingFunction = easing };
        if (completed is not null) animation.Completed += (_, _) => completed();
        if (target is Animatable animatable)
        {
            animatable.BeginAnimation(property, null);
            target.SetValue(property, current);
            animatable.BeginAnimation(property, animation);
        }
        else if (target is UIElement element)
        {
            element.BeginAnimation(property, null);
            element.SetValue(property, current);
            element.BeginAnimation(property, animation);
        }
    }
}
