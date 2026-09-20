namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Media;

internal static class ToastTransitions
{
    internal static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(180);

    internal static void BeginEntrance(FrameworkElement card, bool animationsEnabled) =>
        CardTransitions.BeginEntrance(card, animationsEnabled, Duration, 0.98);

    internal static CardTransitions.ExitHandle BeginExit(
        FrameworkElement card,
        bool animationsEnabled,
        Action completed) =>
        CardTransitions.BeginExit(card, animationsEnabled, completed, Duration);

    internal static (ScaleTransform Scale, TranslateTransform Translate) GetTransforms(
        FrameworkElement card) => CardTransitions.GetTransforms(card);

    internal static void Settle(FrameworkElement card) => CardTransitions.Settle(card);
}
