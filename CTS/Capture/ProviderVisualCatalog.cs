using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CircleToSearch.Search;
using CircleToSearch.Ui;

namespace CircleToSearch.Capture;

internal static class ProviderVisualCatalog
{
    private const double SearchMarkSize = 18;

    internal static FrameworkElement CreateSearchMark(string providerId, bool lightTheme, bool textSearch = false)
    {
        var palette = PluginPalette.For(lightTheme).Provider;
        if (!textSearch || !string.Equals(providerId, SearchProviderIds.TraceMoe, StringComparison.OrdinalIgnoreCase))
            return CreateMark(providerId.ToLowerInvariant(), palette, SearchMarkSize);

        return OverlayVisualResources.BrandMark(SearchMarkSize,
            (Frozen(PluginPalette.AniListBlue), PluginIcons.AniListBlue),
            (Frozen(lightTheme ? palette.Text : PluginPalette.AniListWhite), PluginIcons.AniListLetter));
    }

    public static FrameworkElement Create(
        SearchProviderDescriptor descriptor,
        UiStrings strings,
        bool lightTheme,
        bool includeFullName = false)
    {
        var palette = PluginPalette.For(lightTheme).Provider;
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var mark = CreateMark(descriptor.Id, palette);
        var label = descriptor.Id switch
        {
            SearchProviderIds.GoogleLens => strings.GoogleProviderShortLabel,
            SearchProviderIds.YandexImages => strings.YandexProviderShortLabel,
            _ => descriptor.DisplayName,
        };
        row.Children.Add(mark);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Frozen(includeFullName ? palette.MenuText : palette.Text),
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = OverlayFont,
            FontSize = includeFullName ? 13 : 13.5,
            FontWeight = FontWeights.Medium,
        });
        var detail = descriptor.Id switch
        {
            SearchProviderIds.TraceMoe => strings.TraceMoeProviderDescription,
            _ => descriptor.DisplayName,
        };
        if (includeFullName && !string.Equals(label, detail, StringComparison.Ordinal))
        {
            names.Children.Add(new TextBlock
            {
                Text = detail,
                Foreground = Frozen(palette.MenuMutedText),
                FontFamily = OverlayFont,
                FontSize = 11,
                FontWeight = FontWeights.Normal,
                Opacity = 0.64,
            });
        }
        names.Margin = new Thickness(includeFullName ? 10 : 7, 0, 0, 0);
        row.Children.Add(names);
        return row;
    }

    private static FrameworkElement CreateMark(string providerId, ProviderPalette palette, double size = 20) =>
        providerId switch
        {
            SearchProviderIds.GoogleLens => OverlayVisualResources.BrandMark(size,
                (Frozen(palette.GoogleRed), PluginIcons.GoogleRed),
                (Frozen(palette.Google), PluginIcons.GoogleBlue),
                (Frozen(palette.GoogleYellow), PluginIcons.GoogleYellow),
                (Frozen(palette.GoogleGreen), PluginIcons.GoogleGreen)),
            SearchProviderIds.YandexImages => OverlayVisualResources.BrandMark(size,
                (Frozen(palette.Yandex), PluginIcons.YandexLetter)),
            SearchProviderIds.TraceMoe => OverlayVisualResources.BrandMark(size,
                (Frozen(palette.Trace), PluginIcons.TraceMoe)),
            _ => OverlayVisualResources.BrandMark(size,
                (Frozen(palette.Neutral), NeutralMark)),
        };

    private static readonly FontFamily OverlayFont = new("Segoe UI Variable Text");
    private static readonly Geometry NeutralMark = CreateNeutralMark();

    private static Geometry CreateNeutralMark()
    {
        var geometry = new EllipseGeometry(new Point(12, 12), 10, 10);
        geometry.Freeze();
        return geometry;
    }

    private static SolidColorBrush Frozen(Color color) => OverlayVisualResources.Frozen(color);
}
