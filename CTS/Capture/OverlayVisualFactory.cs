namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using CircleToSearch.Search;
using CircleToSearch.Ui;
using CircleToSearch.Ui.Effects;

public static class OverlayVisualFactory
{
    public static OverlayVisual CreateRoot(
        BitmapSource? frame,
        Size size,
        double chipBottomMargin,
        UiStrings strings) =>
        CreateRoot(frame, size, chipBottomMargin, SystemTheme.IsLight(), strings);

    internal static OverlayVisual CreateRoot(
        BitmapSource? frame,
        Size size,
        double chipBottomMargin,
        bool lightTheme,
        UiStrings strings) =>
        CreateRoot(frame, size, chipBottomMargin, lightTheme, strings, [], null);

    internal static OverlayVisual CreateRoot(
        BitmapSource? frame,
        Size size,
        double chipBottomMargin,
        bool lightTheme,
        UiStrings strings,
        IReadOnlyList<SearchProviderDescriptor> providers,
        string? selectedProviderId)
    {
        var palette = PluginPalette.For(lightTheme);
        var selection = SelectionOverlayVisualFactory.Create(frame, size);
        var provider = ProviderMenuVisualFactory.Create(providers, selectedProviderId, lightTheme, strings);
        var music = MusicOverlayVisualFactory.Create(size, lightTheme, strings);
        var debug = DebugOverlayVisualFactory.Create(lightTheme, strings);
        var actions = ActionTrayVisualFactory.Create(
            palette.SelectionChip,
            strings,
            provider,
            music);
        var bottom = BottomOverlayVisualFactory.Create(chipBottomMargin, actions, provider, music);
        var sceneRippleLayer = new Canvas { IsHitTestVisible = false };
        var effects = new OverlayEffectsVisual(
            sceneRippleLayer,
            new SceneRippleHost(sceneRippleLayer));

        var root = new Grid();
        root.Children.Add(selection.Screenshot);
        root.Children.Add(selection.Dim);
        root.Children.Add(selection.DimRect);
        root.Children.Add(selection.Sheen);
        root.Children.Add(selection.Halo);
        root.Children.Add(selection.Accent);
        root.Children.Add(selection.SelectionFrame);
        root.Children.Add(selection.InputSurface);
        root.Children.Add(effects.SceneRippleLayer);
        root.Children.Add(music.ListeningLayer);
        root.Children.Add(bottom.Root);
        root.Children.Add(debug.Panel);

        return new OverlayVisual(lightTheme, root, selection, actions, provider, music, debug, bottom, effects);
    }
}
