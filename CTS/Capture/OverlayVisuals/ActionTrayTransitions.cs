namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Media;

public static class ActionTrayTransitions
{
    private const double ChipEntranceLift = 24;
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(160);

    public static void BeginEntrance(ActionTrayVisual visual)
    {
        if (!OverlayVisualResources.AnimationsEnabled())
        {
            visual.Tray.Opacity = 1;
            visual.Lift.Y = 0;
            return;
        }
        visual.Tray.BeginAnimation(
            UIElement.OpacityProperty,
            OverlayVisualResources.Animate(0, 1, OverlayVisualResources.EntranceDuration));
        visual.Lift.BeginAnimation(
            TranslateTransform.YProperty,
            OverlayVisualResources.Animate(ChipEntranceLift, 0, OverlayVisualResources.EntranceDuration));
    }

    public static void BeginExit(ActionTrayVisual visual) =>
        BeginExit(visual, OverlayVisualResources.AnimationsEnabled());

    internal static void BeginExit(ActionTrayVisual visual, bool animationsEnabled)
    {
        visual.Tray.IsHitTestVisible = false;
        var opacity = visual.Tray.Opacity;
        visual.Tray.BeginAnimation(UIElement.OpacityProperty, null);
        visual.Tray.Opacity = opacity;
        if (!animationsEnabled)
        {
            visual.Tray.Opacity = 0;
            return;
        }
        visual.Tray.BeginAnimation(
            UIElement.OpacityProperty,
            OverlayVisualResources.Animate(opacity, 0, ExitDuration));
    }

    public static void BeginReturn(ActionTrayVisual visual) =>
        BeginReturn(visual, OverlayVisualResources.AnimationsEnabled());

    internal static void BeginReturn(ActionTrayVisual visual, bool animationsEnabled)
    {
        var opacity = visual.Tray.Opacity;
        var lift = visual.Lift.Y;
        visual.Tray.BeginAnimation(UIElement.OpacityProperty, null);
        visual.Lift.BeginAnimation(TranslateTransform.YProperty, null);
        visual.Tray.Opacity = opacity;
        visual.Lift.Y = lift;
        visual.Tray.IsHitTestVisible = true;

        if (!animationsEnabled)
        {
            visual.Tray.Opacity = 1;
            visual.Lift.Y = 0;
            return;
        }

        visual.Tray.BeginAnimation(
            UIElement.OpacityProperty,
            OverlayVisualResources.Animate(opacity, 1, OverlayVisualResources.EntranceDuration));
        visual.Lift.BeginAnimation(
            TranslateTransform.YProperty,
            OverlayVisualResources.Animate(lift, 0, OverlayVisualResources.EntranceDuration));
    }
}
