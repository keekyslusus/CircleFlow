namespace CircleToSearch.Capture;

using System.Windows.Controls;
using CircleToSearch.MusicRecognition;
using CircleToSearch.Search;
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

    internal static void SetPinterestMode(
        DebugOverlayVisual visual,
        PinterestDebugMode mode,
        bool lightTheme) =>
        Select(visual.PinterestModeButtons, PluginPalette.For(lightTheme).Card, mode);

    internal static void SetMusicScenario(
        Panel buttons,
        CardPalette palette,
        MusicDebugScenario scenario) =>
        Select(buttons, palette, scenario);

    private static void Select(Panel buttons, CardPalette palette, object tag)
    {
        foreach (var button in buttons.Children.OfType<Button>())
        {
            var selected = Equals(button.Tag, tag);
            button.Background = OverlayVisualResources.Frozen(
                selected ? palette.PrimaryContainer : PluginPalette.Transparent);
            button.Foreground = OverlayVisualResources.Frozen(
                selected ? palette.OnPrimaryContainer : palette.Text);
        }
    }
}
