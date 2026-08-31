namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Controls;

internal static class BottomOverlayVisualFactory
{
    private const double ResultGapDips = 16;

    internal static BottomOverlayVisual Create(
        double chipBottomMargin,
        ActionTrayVisual actions,
        ProviderMenuVisual? provider,
        MusicOverlayVisual music)
    {
        music.ResultHost.Margin = new Thickness(0, 0, 0, ResultGapDips);

        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, chipBottomMargin),
        };
        stack.Children.Add(music.ResultHost);
        stack.Children.Add(actions.Tray);

        var providerMenuLayer = new Canvas();
        if (provider is not null) providerMenuLayer.Children.Add(provider.Menu);

        var root = new Grid();
        root.Children.Add(stack);
        root.Children.Add(providerMenuLayer);
        Panel.SetZIndex(root, 2);
        return new BottomOverlayVisual(root, stack, providerMenuLayer);
    }
}
