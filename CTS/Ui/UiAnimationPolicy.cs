using System.Windows;

namespace CircleToSearch.Ui;

internal static class UiAnimationPolicy
{
    internal static bool Enabled => SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
}
