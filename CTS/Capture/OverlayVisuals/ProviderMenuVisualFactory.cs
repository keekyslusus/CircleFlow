namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using CircleToSearch.Search;
using CircleToSearch.Ui;

internal static class ProviderMenuVisualFactory
{
    internal static ProviderMenuVisual? Create(
        IReadOnlyList<SearchProviderDescriptor> providers,
        string? selectedProviderId,
        bool lightTheme,
        UiStrings strings)
    {
        if (providers.Count == 0) return null;
        var selected = providers.First(provider =>
            string.Equals(provider.Id, selectedProviderId, StringComparison.OrdinalIgnoreCase));
        var palette = PluginPalette.For(lightTheme).Provider;
        var content = new ContentControl
        {
            Content = ProviderVisualCatalog.Create(selected, strings, lightTheme),
            IsHitTestVisible = false,
        };
        var chevron = new Path
        {
            Data = PluginIcons.ChevronDownFilled,
            Width = 6,
            Height = 6,
            Stretch = Stretch.Uniform,
            Fill = OverlayVisualResources.Frozen(palette.Hint),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(),
            IsHitTestVisible = false,
        };
        var chevronSlot = new Grid
        {
            Width = 14,
            Height = 14,
            Margin = new Thickness(7, 0, 0, 0),
            IsHitTestVisible = false,
            Visibility = providers.Count > 1 ? Visibility.Visible : Visibility.Collapsed,
        };
        chevronSlot.Children.Add(chevron);
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(content);
        row.Children.Add(chevronSlot);
        var button = new Button
        {
            Content = row,
            Height = 44,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(12, 0, 13, 0),
            Background = OverlayVisualResources.Frozen(palette.Surface),
            Foreground = OverlayVisualResources.Frozen(palette.Text),
            BorderBrush = OverlayVisualResources.Frozen(palette.Border),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = strings.SelectSearchProvider(selected.DisplayName),
            Effect = OverlayVisualResources.DockShadow(
                palette.Surface.A == 0xF0 ? 8 : 6,
                palette.Surface.A == 0xF0 ? 0.3 : 0.35),
        };
        OverlayVisualResources.ApplyButtonTemplate(button, 22, palette.Hover, palette.Text);
        AutomationProperties.SetName(button, strings.SelectSearchProvider(selected.DisplayName));

        var panel = new StackPanel();
        AddMenuItems(panel, providers, selectedProviderId, lightTheme, strings);
        var menu = new Border
        {
            Child = panel,
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(6),
            Width = 220,
            CornerRadius = new CornerRadius(16),
            Background = OverlayVisualResources.Frozen(palette.MenuSurface),
            BorderBrush = OverlayVisualResources.Frozen(palette.MenuBorder),
            BorderThickness = new Thickness(1),
            RenderTransformOrigin = new Point(0.5, 1),
            Effect = new DropShadowEffect
            {
                Color = PluginPalette.OpaqueBlack,
                BlurRadius = 20,
                ShadowDepth = 10,
                Direction = -90,
                Opacity = palette.MenuShadowOpacity,
            },
        };
        return new ProviderMenuVisual(button, content, chevron, menu);
    }

    internal static void PopulateMenuItems(
        ProviderMenuVisual visual,
        IReadOnlyList<SearchProviderDescriptor> providers,
        string selectedProviderId,
        bool lightTheme,
        UiStrings strings)
    {
        var panel = (StackPanel)visual.Menu.Child;
        panel.Children.Clear();
        AddMenuItems(panel, providers, selectedProviderId, lightTheme, strings);
    }

    private static void AddMenuItems(
        Panel panel,
        IReadOnlyList<SearchProviderDescriptor> providers,
        string? selectedProviderId,
        bool lightTheme,
        UiStrings strings)
    {
        var palette = PluginPalette.For(lightTheme).Provider;
        foreach (var descriptor in providers.Where(provider =>
                     !string.Equals(provider.Id, selectedProviderId, StringComparison.OrdinalIgnoreCase)))
        {
            var item = new Button
            {
                Tag = descriptor.Id,
                Content = ProviderVisualCatalog.Create(descriptor, strings, lightTheme, includeFullName: true),
                Padding = new Thickness(12, 5, 12, 5),
                MinWidth = 206,
                MinHeight = 36,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Foreground = OverlayVisualResources.Frozen(palette.MenuText),
                Background = OverlayVisualResources.Frozen(PluginPalette.Transparent),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = descriptor.DisplayName,
                FontFamily = OverlayVisualResources.Font,
            };
            OverlayVisualResources.ApplyButtonTemplate(item, 12, palette.MenuHover, palette.MenuHoverText);
            AutomationProperties.SetName(item, descriptor.DisplayName);
            panel.Children.Add(item);
        }
    }
}
