namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

internal static class BottomOverlayVisualFactory
{
    private const double ResultGapDips = 16;

    internal static BottomOverlayVisual Create(
        double chipBottomMargin,
        ActionTrayVisual actions,
        ProviderMenuVisual? provider,
        MusicOverlayVisual music)
    {
        music.ResultHost.Margin = new Thickness();
        var resultSlot = new Grid
        {
            Margin = new Thickness(0, 0, 0, ResultGapDips),
        };
        resultSlot.SetBinding(
            UIElement.VisibilityProperty,
            new Binding(nameof(UIElement.Visibility)) { Source = music.ResultHost });
        resultSlot.Children.Add(music.ResultHost);
        var actionSlot = new Grid();
        actionSlot.Children.Add(actions.Tray);

        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, chipBottomMargin),
        };
        stack.Children.Add(resultSlot);
        stack.Children.Add(actionSlot);

        var providerMenuLayer = new Canvas();
        if (provider is not null) providerMenuLayer.Children.Add(provider.Menu);

        var root = new Grid();
        root.Children.Add(stack);
        root.Children.Add(providerMenuLayer);
        Panel.SetZIndex(root, 2);
        var layoutTransitions = new BottomOverlayLayoutTransitions(root, stack);
        return new BottomOverlayVisual(
            root,
            stack,
            resultSlot,
            actionSlot,
            providerMenuLayer,
            layoutTransitions);
    }
}
