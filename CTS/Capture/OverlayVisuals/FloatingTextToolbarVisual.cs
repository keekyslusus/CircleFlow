namespace CircleToSearch.Capture;

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using CircleToSearch.Ui;

public sealed class FloatingTextToolbarVisual
{
    private const double ToolbarHeight = 36;
    private const double EdgeMargin = 12;

    public Border Root { get; }
    public Button CopyButton { get; }
    public Button SearchButton { get; }
    public Button TranslateButton { get; }

    public FloatingTextToolbarVisual(bool lightTheme, UiStrings strings)
    {
        var palette = PluginPalette.For(lightTheme);
        var tbPalette = palette.FloatingToolbar;

        CopyButton = CreateToolbarButton(
            CreateCopyIconGeometry(),
            strings.CopyTextAction,
            "Ctrl+C",
            tbPalette);

        SearchButton = CreateToolbarButton(
            CreateSearchIconGeometry(),
            strings.SearchTextAction,
            null,
            tbPalette);

        TranslateButton = CreateToolbarButton(
            CreateTranslateIconGeometry(),
            strings.TranslateAction,
            null,
            tbPalette);

        var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(CopyButton);
        stack.Children.Add(CreateDivider(palette.SelectionChip.Divider));
        stack.Children.Add(SearchButton);
        stack.Children.Add(CreateDivider(palette.SelectionChip.Divider));
        stack.Children.Add(TranslateButton);

        Root = new Border
        {
            Child = stack,
            Height = ToolbarHeight,
            Padding = new Thickness(6, 2, 6, 2),
            Background = OverlayVisualResources.Frozen(tbPalette.Surface),
            BorderBrush = OverlayVisualResources.Frozen(tbPalette.Border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(ToolbarHeight / 2),
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Effect = new DropShadowEffect
            {
                Color = PluginPalette.OpaqueBlack,
                BlurRadius = 16,
                ShadowDepth = 4,
                Direction = -90,
                Opacity = tbPalette.ShadowOpacity,
            },
        };
    }

    public void ShowAt(Rect selectionBounds, Size windowSize)
    {
        Root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var toolbarWidth = Root.DesiredSize.Width > 0 ? Root.DesiredSize.Width : 220;

        var x = selectionBounds.Left + (selectionBounds.Width - toolbarWidth) / 2;
        x = Math.Clamp(x, EdgeMargin, Math.Max(EdgeMargin, windowSize.Width - toolbarWidth - EdgeMargin));

        var y = selectionBounds.Top - ToolbarHeight - 8;
        if (y < EdgeMargin)
        {
            y = selectionBounds.Bottom + 8;
        }
        y = Math.Clamp(y, EdgeMargin, Math.Max(EdgeMargin, windowSize.Height - ToolbarHeight - EdgeMargin));

        Root.Margin = new Thickness(x, y, 0, 0);
        Root.Visibility = Visibility.Visible;
    }

    public void Hide()
    {
        Root.Visibility = Visibility.Collapsed;
    }

    private static Button CreateToolbarButton(
        Geometry iconGeometry,
        string labelText,
        string? keycapText,
        FloatingToolbarPalette palette)
    {
        var icon = new Path
        {
            Data = iconGeometry,
            Fill = OverlayVisualResources.Frozen(palette.Text),
            Width = 14,
            Height = 14,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };

        var label = new TextBlock
        {
            Text = labelText,
            FontFamily = OverlayVisualResources.Font,
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = OverlayVisualResources.Frozen(palette.Text),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(icon);
        row.Children.Add(label);

        if (!string.IsNullOrEmpty(keycapText))
        {
            var keycap = new Border
            {
                Background = OverlayVisualResources.Frozen(Color.FromArgb(0x18, palette.Text.R, palette.Text.G, palette.Text.B)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = keycapText,
                    FontFamily = OverlayVisualResources.Font,
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = OverlayVisualResources.Frozen(palette.Text),
                },
            };
            row.Children.Add(keycap);
        }

        var button = new Button
        {
            Content = row,
            Padding = new Thickness(8, 4, 8, 4),
            Background = OverlayVisualResources.Frozen(PluginPalette.Transparent),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Focusable = false,
        };
        OverlayVisualResources.ApplyButtonTemplate(button, 14, palette.Hover, palette.Text);
        return button;
    }

    private static Rectangle CreateDivider(Color color) => new()
    {
        Width = 1,
        Height = 16,
        Fill = OverlayVisualResources.Frozen(color),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(4, 0, 4, 0),
    };

    private static Geometry CreateCopyIconGeometry()
    {
        var g = Geometry.Parse("M16 1H4c-1.1 0-2 .9-2 2v14h2V3h12V1zm3 4H8c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h11c1.1 0 2-.9 2-2V7c0-1.1-.9-2-2-2zm0 16H8V7h11v14z");
        g.Freeze();
        return g;
    }

    private static Geometry CreateSearchIconGeometry()
    {
        var g = Geometry.Parse("M15.5 14h-.79l-.28-.27C15.41 12.59 16 11.11 16 9.5 16 5.91 13.09 3 9.5 3S3 5.91 3 9.5 5.91 16 9.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z");
        g.Freeze();
        return g;
    }

    private static Geometry CreateTranslateIconGeometry()
    {
        var g = Geometry.Parse("M12.87 15.07l-2.54-2.51.03-.08c1.74-1.94 2.98-4.17 3.71-6.49H17V4h-7V2H8v2H1v1.99h11.17C11.5 7.92 10.44 9.75 9 11.35 8.07 10.32 7.3 9.19 6.69 8h-2c.73 1.63 1.73 3.17 2.98 4.56l-5.09 5.02L4 19l5-5 3.11 3.11.76-2.04zM18.5 10h-2L12 22h2l1.12-3h4.75L21 22h2l-4.5-12zm-2.62 7l1.62-4.33L19.12 17h-3.24z");
        g.Freeze();
        return g;
    }
}
