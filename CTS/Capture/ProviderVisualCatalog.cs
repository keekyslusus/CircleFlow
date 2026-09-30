using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CircleToSearch.Search;
using CircleToSearch.Ui;

namespace CircleToSearch.Capture;

internal static class ProviderVisualCatalog
{
    private const double SearchMarkSize = 18;

    internal static FrameworkElement CreateSearchMark(string providerId, bool lightTheme) =>
        CreateMark(providerId.ToLowerInvariant(), PluginPalette.For(lightTheme).Provider, SearchMarkSize);

    internal static FrameworkElement CreateTextSearchMark(string providerId, string engineId, bool lightTheme)
    {
        var palette = PluginPalette.For(lightTheme).Provider;
        return TextSearchEngines.Find(engineId)?.Id switch
        {
            TextSearchEngines.Bing => OverlayVisualResources.BrandMark(SearchMarkSize,
                (BingUpperFill, PluginIcons.BingUpper),
                (BingLowerFill, PluginIcons.BingLower),
                (BingStemFill, PluginIcons.BingStem)),
            TextSearchEngines.DuckDuckGo => OverlayVisualResources.BrandMark(SearchMarkSize,
                (Frozen(PluginPalette.DuckDuckGoOrange), PluginIcons.DuckDuckGoDisc),
                (Frozen(PluginPalette.DuckDuckGoFeather), PluginIcons.DuckDuckGoBody),
                (Frozen(PluginPalette.DuckDuckGoWhite), PluginIcons.DuckDuckGoRing),
                (Frozen(PluginPalette.DuckDuckGoTie), PluginIcons.DuckDuckGoTie),
                (Frozen(PluginPalette.DuckDuckGoTieHighlight), PluginIcons.DuckDuckGoTieHighlight),
                (Frozen(PluginPalette.DuckDuckGoBeak), PluginIcons.DuckDuckGoBeak),
                (Frozen(PluginPalette.DuckDuckGoEyes), PluginIcons.DuckDuckGoBrows),
                (Frozen(PluginPalette.DuckDuckGoEyes), PluginIcons.DuckDuckGoEyes)),
            TextSearchEngines.Google => CreateMark(SearchProviderIds.GoogleLens, palette, SearchMarkSize),
            TextSearchEngines.Kagi => OverlayVisualResources.BrandMark(SearchMarkSize,
                (Frozen(PluginPalette.KagiWhite), PluginIcons.KagiHandle),
                (Frozen(PluginPalette.KagiInk), PluginIcons.KagiHandleOutline),
                (Frozen(PluginPalette.KagiWhite), PluginIcons.KagiLens),
                (Frozen(PluginPalette.KagiInk), PluginIcons.KagiLensOutline),
                (Frozen(PluginPalette.KagiYellow), PluginIcons.KagiCore),
                (Frozen(PluginPalette.KagiInk), PluginIcons.KagiCoreOutline),
                (Frozen(PluginPalette.KagiInk), PluginIcons.KagiSeamsOutline)),
            TextSearchEngines.Qwant => OverlayVisualResources.BrandMark(SearchMarkSize,
                (Frozen(palette.Qwant), PluginIcons.QwantMark)),
            TextSearchEngines.Startpage => OverlayVisualResources.BrandMark(SearchMarkSize,
                (Frozen(PluginPalette.StartpageViolet), PluginIcons.StartpageMark)),
            _ when string.Equals(providerId, SearchProviderIds.TraceMoe, StringComparison.OrdinalIgnoreCase) =>
                OverlayVisualResources.BrandMark(SearchMarkSize,
                    (Frozen(PluginPalette.AniListBlue), PluginIcons.AniListBlue),
                    (Frozen(lightTheme ? palette.Text : PluginPalette.AniListWhite), PluginIcons.AniListLetter)),
            _ when string.Equals(providerId, SearchProviderIds.Pinterest, StringComparison.OrdinalIgnoreCase) =>
                CreateMark(SearchProviderIds.GoogleLens, palette, SearchMarkSize),
            _ => CreateSearchMark(providerId, lightTheme),
        };
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
            SearchProviderIds.Pinterest => strings.PinterestProviderDescription,
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
            SearchProviderIds.Pinterest => OverlayVisualResources.BrandMark(size,
                (Frozen(PluginPalette.PinterestRed), PluginIcons.PinterestMark)),
            _ => OverlayVisualResources.BrandMark(size,
                (Frozen(palette.Neutral), NeutralMark)),
        };

    private static readonly FontFamily OverlayFont = new("Segoe UI Variable Text");

    // Gradient geometry comes from the Bing SVG, scaled and centered with the mark in its 24x24 frame.
    private static readonly Brush BingUpperFill = RadialGradient(PluginPalette.BingTeal, PluginPalette.BingBlue,
        new Matrix(-7.26468, -5.61925, 11.6517, -6.76684, 7.41528, -11.509));
    private static readonly Brush BingLowerFill = RadialGradient(PluginPalette.BingSky, PluginPalette.BingNavy,
        new Matrix(11.02937, -7.33651, 2.83589, 10.27353, 10.45499, 17.75145));
    private static readonly Brush BingStemFill = Frozen(new LinearGradientBrush(
        PluginPalette.BingSky, PluginPalette.BingNavy, new Point(7.38141, 2), new Point(7.38141, 19.27349))
    {
        MappingMode = BrushMappingMode.Absolute,
    });
    private static readonly Geometry NeutralMark = CreateNeutralMark();

    private static Geometry CreateNeutralMark()
    {
        var geometry = new EllipseGeometry(new Point(12, 12), 10, 10);
        geometry.Freeze();
        return geometry;
    }

    private static SolidColorBrush Frozen(Color color) => OverlayVisualResources.Frozen(color);

    private static Brush RadialGradient(Color center, Color edge, Matrix transform) => Frozen(new RadialGradientBrush(center, edge)
    {
        MappingMode = BrushMappingMode.Absolute,
        Center = new Point(0, 0),
        GradientOrigin = new Point(0, 0),
        RadiusX = 1,
        RadiusY = 1,
        Transform = new MatrixTransform(transform),
    });

    private static T Frozen<T>(T brush) where T : Brush
    {
        brush.Freeze();
        return brush;
    }
}
