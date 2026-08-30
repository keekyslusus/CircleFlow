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

    public static void BeginExit(ActionTrayVisual visual)
    {
        visual.Tray.IsHitTestVisible = false;
        if (!OverlayVisualResources.AnimationsEnabled())
        {
            visual.Tray.Opacity = 0;
            return;
        }
        visual.Tray.BeginAnimation(
            UIElement.OpacityProperty,
            OverlayVisualResources.Animate(1, 0, ExitDuration));
    }
}
