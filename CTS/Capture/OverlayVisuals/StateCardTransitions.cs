namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Media;

internal static class StateCardTransitions
{
    internal static readonly TimeSpan EntranceDuration = TimeSpan.FromMilliseconds(200);
    internal static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(160);

    internal static void BeginEntrance(FrameworkElement card, bool animationsEnabled) =>
        CardTransitions.BeginEntrance(card, animationsEnabled, EntranceDuration, 0.96);

    internal static CardTransitions.ExitHandle BeginExit(
        FrameworkElement card,
        bool animationsEnabled,
        Action completed) =>
        CardTransitions.BeginExit(card, animationsEnabled, completed, ExitDuration);

    internal static (ScaleTransform Scale, TranslateTransform Translate) GetTransforms(
        FrameworkElement card) => CardTransitions.GetTransforms(card);
}
