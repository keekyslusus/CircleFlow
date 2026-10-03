namespace CircleToSearch.Capture;

using System.Windows.Controls;
using CircleToSearch.MusicRecognition;
using CircleToSearch.Ui;

internal static class DebugOverlayVisualPresenter
{
    internal static void SetMusicScenario(
        DebugOverlayVisual visual,
        MusicDebugScenario scenario,
        bool lightTheme) =>
        SetMusicScenario(
            visual.MusicScenarioButtons,
            PluginPalette.For(lightTheme).Card,
            scenario);

    internal static void SetMusicScenario(
        Panel buttons,
        CardPalette palette,
        MusicDebugScenario scenario)
    {
        foreach (var button in buttons.Children.OfType<Button>())
        {
            var selected = Equals(button.Tag, scenario);
            button.Background = OverlayVisualResources.Frozen(
                selected ? palette.PrimaryContainer : PluginPalette.Transparent);
            button.Foreground = OverlayVisualResources.Frozen(
                selected ? palette.OnPrimaryContainer : palette.Text);
        }
    }
}
