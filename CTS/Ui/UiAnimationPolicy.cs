using System.Windows;

namespace CircleToSearch.Ui;

public static class UiAnimationPolicy
{
    internal static bool Enabled => SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;

    public static Duration ToggleTransitionDuration => new(Enabled ? TimeSpan.FromMilliseconds(180) : TimeSpan.Zero);
}
