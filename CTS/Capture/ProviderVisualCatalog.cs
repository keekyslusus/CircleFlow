using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CircleToSearch.Search;
using CircleToSearch.Ui;

namespace CircleToSearch.Capture;

internal static class ProviderVisualCatalog
{
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
        if (includeFullName && !string.Equals(label, descriptor.DisplayName, StringComparison.Ordinal))
        {
            names.Children.Add(new TextBlock
            {
                Text = descriptor.DisplayName,
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

    private static FrameworkElement CreateMark(string providerId, ProviderPalette palette)
    {
        if (providerId == SearchProviderIds.GoogleLens)
        {
            var drawing = new DrawingGroup();
            drawing.Children.Add(Draw(palette.GoogleRed, GoogleRed));
            drawing.Children.Add(Draw(palette.Google, GoogleBlue));
            drawing.Children.Add(Draw(palette.GoogleYellow, GoogleYellow));
            drawing.Children.Add(Draw(palette.GoogleGreen, GoogleGreen));
            drawing.Freeze();
            return new Image
            {
                Source = new DrawingImage(drawing),
                Width = 16,
                Height = 16,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        if (providerId == SearchProviderIds.YandexImages)
        {
            return new TextBlock
            {
                Width = 16,
                Text = "Я",
                FontFamily = OverlayFont,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = Frozen(palette.Yandex),
                TextAlignment = TextAlignment.Center,
                LineHeight = 16,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        return new Ellipse
        {
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = Frozen(palette.Neutral),
        };
    }

    private static GeometryDrawing Draw(Color color, Geometry geometry)
    {
        var drawing = new GeometryDrawing(Frozen(color), null, geometry);
        drawing.Freeze();
        return drawing;
    }

    private static Geometry FrozenGeometry(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }

    private static readonly FontFamily OverlayFont = new("Segoe UI Variable Text");
    private static readonly Geometry GoogleRed = FrozenGeometry(
        "M24 9.5c3.54 0 6.71 1.22 9.21 3.6l6.85-6.85C35.9 2.38 30.47 0 24 0 14.62 0 6.51 5.38 2.56 13.22l7.98 6.19C12.43 13.72 17.74 9.5 24 9.5z");
    private static readonly Geometry GoogleBlue = FrozenGeometry(
        "M46.98 24.55c0-1.57-.15-3.09-.38-4.55H24v9.02h12.94c-.58 2.96-2.26 5.48-4.78 7.18l7.73 6c4.51-4.18 7.09-10.36 7.09-17.65z");
    private static readonly Geometry GoogleYellow = FrozenGeometry(
        "M10.53 28.59c-.48-1.45-.76-2.99-.76-4.59s.27-3.14.76-4.59l-7.98-6.19C.92 16.46 0 20.12 0 24c0 3.88.92 7.54 2.56 10.78l7.97-6.19z");
    private static readonly Geometry GoogleGreen = FrozenGeometry(
        "M24 48c6.48 0 11.93-2.13 15.89-5.81l-7.73-6c-2.15 1.45-4.92 2.3-8.16 2.3-6.26 0-11.57-4.22-13.47-9.91l-7.98 6.19C6.51 42.62 14.62 48 24 48z");

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
